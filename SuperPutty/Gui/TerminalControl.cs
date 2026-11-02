using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DarkModeForms;
using log4net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// The built-in terminal: a session tab's terminal drawn by SuperPuTTY instead of an embedded PuTTY window, so
    /// device output can be highlighted live (the Highlighted Output rules) and themed. plink.exe, beside PuTTY, makes
    /// the connection with the session's PuTTY profile, keys and jump host (PuttyStartInfo.PlinkCommandLine), running
    /// in a pseudo console (Utils.ConPty); its output is drawn by xterm.js in a WebView2 (web\terminal\index.html).
    /// ctlPuttyPanel creates it as its ApplicationPanel's embedded application when Tools -> Options -> Terminal ->
    /// "Use the built-in terminal" is on, as Gui.RdpControl is for RDP. It writes the session log (from what the page
    /// shows, see docs/MonacoTerminal.md), keeps the text WAITFOR matches against, types Send Commands and button-bar
    /// text, and reconnects a dropped session in the same tab.
    /// </summary>
    public class TerminalControl : UserControl, IEmbeddedApplication, ICommandReceiver
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TerminalControl));

        /// <summary>The page's address, under the host mapped to the web folder</summary>
        private const string PageUrl = "https://" + OutputHighlighting.HostName + "/terminal/index.html";

        /// <summary>The most output sent to the page at once; the next waits for the page's ack</summary>
        private const int ChunkBytes = 256 * 1024;

        /// <summary>How much recent output WAITFOR keeps in memory, in characters</summary>
        private const int RecentOutputLimit = 1024 * 1024;

        /// <summary>A session that ran this long before dropping starts its reconnect attempts afresh</summary>
        private static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(60);

        /// <summary>Protocols plink can connect; the others keep their own programs</summary>
        private static readonly ConnectionProtocol[] Protocols =
        {
            ConnectionProtocol.SSH, ConnectionProtocol.Telnet, ConnectionProtocol.Rlogin, ConnectionProtocol.Raw, ConnectionProtocol.Serial,
            ConnectionProtocol.WINCMD, ConnectionProtocol.PS, ConnectionProtocol.WSL
        };

        /// <summary>
        /// Whether a protocol is a local shell (cmd, PowerShell, WSL), run directly in the pseudo console instead of
        /// through plink
        /// </summary>
        /// <param name="protocol">The session's protocol</param>
        /// <returns>true for cmd, PowerShell and WSL</returns>
        public static bool IsLocalShell(ConnectionProtocol protocol)
        {
            return protocol == ConnectionProtocol.WINCMD || protocol == ConnectionProtocol.PS || protocol == ConnectionProtocol.WSL;
        }

        /// <summary>The session, placeholders resolved; never a session from the tree (it would be saved)</summary>
        private readonly SessionData session;

        /// <summary>The session log this terminal writes (SessionLog), read by WAITFOR, Search Output and Highlighted Output</summary>
        private readonly string logPath;

        /// <summary>The session log this terminal writes (ctlPuttyPanel.SessionLogPath)</summary>
        public string LogPath
        {
            get { return this.logPath; }
        }

        /// <summary>The browser control showing the terminal</summary>
        private readonly WebView2 webView;

        /// <summary>Schedules automatic reconnects</summary>
        private readonly Timer reconnectTimer;

        /// <summary>Sends output held back by <see cref="HoldForNextLine"/> once nothing more has come</summary>
        private readonly Timer lineBreakTimer;

        /// <summary>
        /// The longest output ending in a line break waits for the next line. cmd's pseudo console sends Enter's line
        /// break at once and the new prompt in its next frame, about 16 ms later; drawn apart, the screen showed a blank
        /// line where the prompt belonged, sometimes for a second or two before WebView2 put the next frame on screen.
        /// Over SSH the device's prompt can take longer, so this allows for the network too.
        /// </summary>
        private static readonly TimeSpan LineBreakHold = TimeSpan.FromMilliseconds(100);

        /// <summary>Output from plink not yet sent to the page; guarded by itself</summary>
        private readonly MemoryStream pending = new MemoryStream();

        /// <summary>Guards the WAITFOR text: <see cref="recent"/>, <see cref="recentStart"/>, <see cref="committedTotal"/>, <see cref="currentLine"/>, <see cref="mark"/></summary>
        private readonly object outputLock = new object();

        /// <summary>The most recent logged lines, each followed by "\n"</summary>
        private readonly StringBuilder recent = new StringBuilder();

        /// <summary>Where <see cref="recent"/> starts in all the text logged so far</summary>
        private long recentStart;

        /// <summary>How many characters have been logged in all (lines plus "\n")</summary>
        private long committedTotal;

        /// <summary>The line the cursor is on, which isn't logged until the cursor leaves it</summary>
        private string currentLine = string.Empty;

        /// <summary>Where WAITFOR starts looking, as a position in the logged text followed by the current line</summary>
        private long mark;

        /// <summary>plink in its pseudo console; null while disconnected</summary>
        private ConPty pty;

        /// <summary>The session log being written</summary>
        private FileStream log;

        /// <summary>Whether the page has loaded and taken its config</summary>
        private bool pageReady;

        /// <summary>Whether a chunk is with the page and not yet acknowledged</summary>
        private bool waitingForAck;

        /// <summary>Output waiting longer than this, here or in the page, is logged (Delay)</summary>
        private static readonly TimeSpan SlowOutput = TimeSpan.FromMilliseconds(500);

        /// <summary>When the oldest output in <see cref="pending"/> arrived; guarded by <see cref="pending"/></summary>
        private DateTime pendingSince;

        /// <summary>When the chunk the page is drawing was sent (UTC)</summary>
        private DateTime chunkSentAt;

        /// <summary>The size of the chunk the page is drawing</summary>
        private int chunkBytes;

        /// <summary>The terminal's size in characters, from the page</summary>
        private int columns, rows;

        /// <summary>Whether the session is down and the terminal is waiting to reconnect</summary>
        private bool disconnected;

        /// <summary>Reconnect attempts since the session last ran steadily</summary>
        private int reconnectAttempts;

        /// <summary>When plink was last started</summary>
        private DateTime startedAt;

        /// <summary>Set once the control is closing, so plink ending isn't treated as a dropped session</summary>
        private bool closing;

        /// <summary>Raised when the session ends normally (plink exited cleanly, e.g. after "exit"); the tab then closes, as PuTTY's does</summary>
        public event EventHandler Ended;

        /// <summary>Raised when the session drops, starts reconnecting or comes back, so the tab can show it</summary>
        public event EventHandler StateChanged;

        /// <summary>
        /// Whether a session would use the built-in terminal: the option is on, it's a protocol plink connects, and
        /// plink and the WebView2 runtime are there (otherwise PuTTY is used as before)
        /// </summary>
        /// <param name="session">The session being opened</param>
        /// <returns>true to use the built-in terminal</returns>
        public static bool ShouldUse(SessionData session)
        {
            if (puttyForNextTab || !SuperPuTTY.Settings.BuiltInTerminal || Array.IndexOf(Protocols, session.Proto) < 0)
                return false;
            if (!IsLocalShell(session.Proto) && JumpHosts.PlinkPath() == null)
            {
                Log.Warn("The built-in terminal needs plink.exe or klink.exe (Options > Paths, or beside PuTTY); using PuTTY for " + session.SessionId);
                return false;
            }
            if (OutputHighlighting.RuntimeVersion() == null)
            {
                Log.Warn("The built-in terminal needs the WebView2 runtime; using PuTTY for " + session.SessionId);
                return false;
            }
            return true;
        }

        /// <summary>Set while <see cref="OpenInPutty"/> opens a tab, so that tab gets PuTTY's window</summary>
        [ThreadStatic]
        private static bool puttyForNextTab;

        /// <summary>
        /// Open a session in a classic PuTTY tab even though the built-in terminal is on: for what only PuTTY's window
        /// can do, such as its Special Commands (Break, signals), which plink has no way to send
        /// </summary>
        /// <param name="session">The stored session (not a resolved copy: opening resolves it again)</param>
        public static void OpenInPutty(SessionData session)
        {
            puttyForNextTab = true;
            try
            {
                SuperPuTTY.OpenProtoSession(session);
            }
            finally
            {
                puttyForNextTab = false;
            }
        }

        /// <summary>Create the terminal for a session; nothing connects until <see cref="Start"/></summary>
        /// <param name="session">The session with its placeholders resolved (SessionVariables.Resolve)</param>
        /// <param name="logPath">The session log to write (SessionLog.NewLogPath)</param>
        public TerminalControl(SessionData session, string logPath)
        {
            this.session = session;
            this.logPath = logPath;
            this.BackColor = System.Drawing.Color.Black;
            this.webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = System.Drawing.Color.Black };
            this.Controls.Add(this.webView);
            this.reconnectTimer = new Timer();
            this.reconnectTimer.Tick += (s, e) => { this.reconnectTimer.Stop(); this.Reconnect(); };
            this.lineBreakTimer = new Timer();
            this.lineBreakTimer.Tick += (s, e) => { this.lineBreakTimer.Stop(); this.SendPending(); };
            TerminalPreferences.Changed += this.OnPreferencesChanged;
        }

        /// <summary>Whether the session is connected (or connecting); false while it's down</summary>
        public bool IsConnected
        {
            get { return !this.disconnected; }
        }

        /// <summary>Whether an automatic reconnect is scheduled</summary>
        public bool ReconnectScheduled
        {
            get { return this.reconnectTimer.Enabled; }
        }

        /// <summary>Load the page; the session starts once the page reports its size</summary>
        public async void Start()
        {
            // a new session gets the keyboard, as PuTTY's window does, if its tab is still in front once loaded
            this.focusWanted = true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.logPath));
                this.log = new FileStream(this.logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
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
                core.Settings.IsZoomControlEnabled = false;
                // keys like Ctrl+F, Ctrl+P and F5 belong to the session, not the browser
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.SetVirtualHostNameToFolderMapping(OutputHighlighting.HostName, OutputHighlighting.WebFolder, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (s, e) =>
                {
                    Uri uri;
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) || !string.Equals(uri.Host, OutputHighlighting.HostName, StringComparison.OrdinalIgnoreCase))
                        e.Cancel = true;
                };
                core.NewWindowRequested += (s, e) => e.Handled = true;
                core.WebMessageReceived += this.OnWebMessage;
                core.Navigate(PageUrl);
            }
            catch (Exception ex)
            {
                Log.Error("Could not start the built-in terminal for " + this.session.SessionId, ex);
                Messenger.MessageBox("The built-in terminal couldn't start: " + ex.Message, "Terminal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Whether the tab asked for the keyboard before the page could take it; the page gets it once it's ready
        /// </summary>
        private bool focusWanted;

        /// <summary>
        /// Give the terminal the keyboard. A new tab asks before its page has loaded, so the request is remembered
        /// and carried out when the page reports it's ready (<see cref="FocusIfStillActive"/>).
        /// </summary>
        public void FocusApplication()
        {
            if (this.webView.CoreWebView2 == null || !this.pageLoaded)
            {
                this.focusWanted = true;
                return;
            }
            this.webView.Focus();
            this.Post(new { type = "focus" });
        }

        /// <summary>Whether the page has loaded and can take the keyboard</summary>
        private bool pageLoaded;

        /// <summary>
        /// The page has loaded: give it the keyboard if the tab asked while it was loading and is still the one
        /// in front, so opening a session leaves the cursor in it as with PuTTY, without taking the keyboard from a
        /// tab the user has moved to since
        /// </summary>
        private void FocusIfStillActive()
        {
            this.pageLoaded = true;
            if (!this.focusWanted)
                return;
            this.focusWanted = false;
            ctlPuttyPanel panel = this.FindForm() as ctlPuttyPanel;
            if (panel == null || panel.DockPanel == null || panel.DockPanel.ActiveDocument == panel)
            {
                this.FocusApplication();
            }
        }

        /// <summary>Handle a message from the page</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">A JSON object with a "type"</param>
        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            JObject message;
            try
            {
                message = JObject.Parse(e.WebMessageAsJson);
            }
            catch (JsonException ex)
            {
                Log.Warn("Unreadable message from the terminal page", ex);
                return;
            }
            switch ((string)message["type"])
            {
                case "ready":
                    this.SendConfig();
                    this.FocusIfStillActive();
                    break;
                case "size":
                    this.OnSize((int)message["cols"], (int)message["rows"]);
                    break;
                case "input":
                    this.OnInput((string)message["data"]);
                    break;
                case "ack":
                    this.waitingForAck = false;
                    this.Delay("The page took {0} ms to draw {1} bytes of output", this.chunkSentAt, this.chunkBytes);
                    this.SendPending();
                    break;
                case "log":
                    this.OnLog(message["lines"] as JArray, (string)message["current"]);
                    break;
                case "copy":
                    this.SetClipboard((string)message["text"]);
                    break;
                case "bell":
                    System.Media.SystemSounds.Beep.Play();
                    break;
                case "pasteRequest":
                    // the menu's Copy and Paste sends its text; everything else pastes the clipboard
                    string toPaste = (string)message["text"] ?? (Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
                    // after this message is handled: asking first shows a dialog
                    this.BeginInvokeSafe(() => this.Paste(toPaste));
                    break;
                case "openLink":
                    OpenLink((string)message["uri"]);
                    break;
                case "pref":
                    // saved for every terminal tab; TerminalPreferences.Changed sends it to all of them
                    TerminalPreferences.Set((string)message["name"], (string)message["value"]);
                    break;
                case "cleared":
                    // the pseudo console repaints its own copy of the screen after a resize; clear that copy too
                    this.pty?.ClearScreen();
                    break;
                case "openInPutty":
                    ctlPuttyPanel owner = this.FindForm() as ctlPuttyPanel;
                    if (owner != null)
                    {
                        SessionData stored = owner.Session;
                        this.BeginInvokeSafe(() => OpenInPutty(stored));
                    }
                    break;
                case "tab":
                    // closing happens after this message is handled, not while the page is still in its handler
                    string command = (string)message["command"];
                    ctlPuttyPanel tab = this.FindForm() as ctlPuttyPanel;
                    if (tab != null)
                    {
                        this.BeginInvokeSafe(() => tab.RunTabCommand(command));
                    }
                    break;
                case "sendKey":
                    // the menu's Send Key, through the tab like the tab menu's; no key means Other Key... (it asks)
                    string sendKey = (string)message["key"];
                    ctlPuttyPanel keyTab = this.FindForm() as ctlPuttyPanel;
                    if (keyTab != null)
                    {
                        this.BeginInvokeSafe(() =>
                        {
                            if (string.IsNullOrEmpty(sendKey))
                                keyTab.AskAndSendKey();
                            else
                                keyTab.SendKey(sendKey);
                        });
                    }
                    break;
                case "document":
                    ctlPuttyPanel panel = this.FindForm() as ctlPuttyPanel;
                    if (panel != null)
                    {
                        HighlightedOutputPanel.ShowFor(panel);
                    }
                    break;
            }
        }

        /// <summary>Send a message object to the page as JSON</summary>
        /// <param name="message">An object with a "type" property</param>
        private void Post(object message)
        {
            if (this.webView.CoreWebView2 != null && !this.IsDisposed)
            {
                this.webView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(message));
            }
        }

        /// <summary>Send the page the rule sets and themes, as for Highlighted Output</summary>
        private void SendConfig()
        {
            JArray ruleSets, themes;
            List<string> problems = OutputHighlighting.Load(out ruleSets, out themes);
            if (problems.Count > 0)
            {
                Log.Warn("Highlighting files skipped: " + string.Join("; ", problems));
            }
            bool dark = SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;
            this.Post(new { type = "config", ruleSets, themes, dark, bell = BellStyle(this.session.PuttySession), bellOverload = BellOverload(this.session.PuttySession), cursor = CursorFromProfile(this.session.PuttySession), local = IsLocalShell(this.session.Proto),
                profileFont = FontFromProfile(this.session.PuttySession), fonts = MonospaceFonts(), prefs = TerminalPreferences.All(), cursorColor = this.cursorColor,
                keys = KeysFromProfile(this.session.PuttySession), sessionRules = this.session.HighlightRules ?? string.Empty,
                unlimitedScrollback = TerminalPreferences.UnlimitedScrollback,
                menuKeys = Array.ConvertAll(TerminalKeys.MenuKeys, k => new { key = k[0], label = k[1], hint = k[2] }) });
        }

        /// <summary>
        /// The keys PuTTY sends that differ between profiles (Terminal > Keyboard), as PuTTY saves them: Backspace
        /// ("BackspaceIsDelete": 1 Control-? (DEL), 0 Control-H), Home and End ("RXVTHomeEnd": 0 standard, 1 rxvt) and
        /// the function keys ("LinuxFunctionKeys": 0 ESC[n~, 1 Linux, 2 Xterm R6, 3 VT400, 4 VT100+, 5 SCO, 6 Xterm
        /// 216+). A profile that doesn't set one gets PuTTY's default. The page sends them instead of xterm's own.
        /// </summary>
        /// <param name="profile">The PuTTY profile; empty for Default Settings</param>
        /// <returns>backspace ("del" or "bs"), homeEnd ("standard" or "rxvt") and functionKeys ("tilde", "linux", "xtermr6", "vt400", "vt100", "sco" or "xterm216")</returns>
        public static object KeysFromProfile(string profile)
        {
            int backspace, homeEnd, function;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "BackspaceIsDelete"), out backspace))
                backspace = 1;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "RXVTHomeEnd"), out homeEnd))
                homeEnd = 0;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "LinuxFunctionKeys"), out function))
                function = 0;
            string[] functionNames = { "tilde", "linux", "xtermr6", "vt400", "vt100", "sco", "xterm216" };
            return new
            {
                backspace = backspace == 0 ? "bs" : "del",
                homeEnd = homeEnd == 1 ? "rxvt" : "standard",
                functionKeys = function >= 0 && function < functionNames.Length ? functionNames[function] : "tilde"
            };
        }

        /// <summary>
        /// Paste into the session. A paste of at least Options -> Terminal's number of lines (2 by default) is shown first
        /// in <see cref="dlgPasteCheck"/>, where it can be checked and changed: each line runs as it arrives, so 40 lines
        /// of config pasted by mistake into a switch would all be applied.
        /// </summary>
        /// <param name="text">The text to paste; nothing happens when it's empty</param>
        private void Paste(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            int threshold;
            int.TryParse(TerminalPreferences.Get("pasteLines"), out threshold);
            if (ShouldCheckPaste(threshold, PasteLineCount(text)))
            {
                using (dlgPasteCheck dialog = new dlgPasteCheck(text, this.session.SessionName))
                {
                    DialogResult answer = dialog.ShowDialog(this.FindForm());
                    text = dialog.PasteText;
                    this.Post(new { type = "focus" });
                    if (answer != DialogResult.OK || text.Length == 0)
                        return;
                }
            }
            this.Post(new { type = "paste", text });
        }

        /// <summary>
        /// Open a link Ctrl+clicked in the output in the default browser. Only web links: the output comes from the
        /// device, so a file: or other link there mustn't run anything.
        /// </summary>
        /// <param name="uri">The link as the page found it</param>
        private static void OpenLink(string uri)
        {
            if (!IsWebLink(uri))
            {
                Log.WarnFormat("Not opening a link that isn't http or https");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new Uri(uri).AbsoluteUri);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Log.Warn("Could not open a link from the terminal", ex);
            }
        }

        /// <summary>Whether a link is an absolute http or https address, the only kind <see cref="OpenLink"/> opens</summary>
        /// <param name="uri">The link</param>
        /// <returns>true for http and https</returns>
        public static bool IsWebLink(string uri)
        {
            Uri parsed;
            return Uri.TryCreate(uri, UriKind.Absolute, out parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>How many lines a paste would type; a line break at the very end doesn't start another</summary>
        /// <param name="text">The text</param>
        /// <returns>The number of lines, at least 1</returns>
        public static int PasteLineCount(string text)
        {
            string lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (lines.EndsWith("\n"))
            {
                lines = lines.Substring(0, lines.Length - 1);
            }
            return lines.Split('\n').Length;
        }

        /// <summary>Whether to show a paste in <see cref="dlgPasteCheck"/> before typing it</summary>
        /// <param name="threshold">The pasteLines preference: the fewest lines that are shown first; 0 or less never</param>
        /// <param name="lines">How many lines the paste is (<see cref="PasteLineCount"/>)</param>
        /// <returns>true when the paste has at least <paramref name="threshold"/> lines</returns>
        public static bool ShouldCheckPaste(int threshold, int lines)
        {
            return threshold > 0 && lines >= threshold;
        }

        /// <summary>
        /// A terminal preference changed (in this tab's menu, another tab's, or Tools -> Options -> Terminal): send them
        /// all to the page, which applies them
        /// </summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        private void OnPreferencesChanged(object sender, EventArgs e)
        {
            if (this.pageLoaded)
            {
                this.Post(new { type = "prefs", prefs = TerminalPreferences.All() });
            }
        }

        /// <summary>The session's environment colour as #rrggbb, for the cursor; null for none</summary>
        private string cursorColor;

        /// <summary>When the session log last got a time stamp (<see cref="LogStamp"/>)</summary>
        private DateTime lastLogStamp = DateTime.MinValue;

        /// <summary>
        /// Draw the cursor in the session's environment colour (red for production), like its tab, or the theme's
        /// cursor colour when it has none. The cursor rather than the prompt: it's the same on every device, where
        /// prompts can't be recognised reliably. ctlPuttyPanel calls this when the tab opens and whenever colours change.
        /// </summary>
        /// <param name="color">The colour (EnvironmentColors.For), or null</param>
        public void SetCursorColor(System.Drawing.Color? color)
        {
            this.cursorColor = color.HasValue ? string.Format("#{0:x2}{1:x2}{2:x2}", color.Value.R, color.Value.G, color.Value.B) : null;
            if (this.pageLoaded)
            {
                this.Post(new { type = "cursorColor", color = this.cursorColor });
            }
        }

        /// <summary>
        /// What the bell should do, from the session's PuTTY profile (Terminal > Bell) as PuTTY would. PuTTY saves
        /// it as "Beep": 0 none, 1 the default alert sound, 2 visual, 3 a sound file, 4 the PC speaker. Returns
        /// "none", "visual" (flash) or "sound" (the system's alert sound, for all three sound choices). A profile
        /// that doesn't set it gets visual.
        /// </summary>
        /// <param name="profile">The PuTTY profile; empty for Default Settings</param>
        /// <returns>"none", "visual" or "sound"</returns>
        public static string BellStyle(string profile)
        {
            int type;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "Beep"), out type))
                return "visual";
            switch (type)
            {
                case 0: return "none";
                case 2: return "visual";
                default: return "sound";
            }
        }

        /// <summary>
        /// The cursor from the session's PuTTY profile (Window > Appearance), as PuTTY saves it: "CurType" (0 block,
        /// 1 underline, 2 vertical line) and "BlinkCur" (1 to blink). Missing values get PuTTY's defaults: a steady block.
        /// </summary>
        /// <param name="profile">The PuTTY profile; empty for Default Settings</param>
        /// <returns>An object for the page: style ("block", "underline" or "bar") and blink</returns>
        public static object CursorFromProfile(string profile)
        {
            int type, blink;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "CurType"), out type))
                type = 0;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "BlinkCur"), out blink))
                blink = 0;
            return new { style = type == 1 ? "underline" : type == 2 ? "bar" : "block", blink = blink != 0 };
        }

        /// <summary>
        /// The font from the session's PuTTY profile (Window > Appearance), as PuTTY saves it: "Font" (its name) and
        /// "FontHeight" (in points). Missing values get PuTTY's default, Courier New 10 point.
        /// </summary>
        /// <param name="profile">The PuTTY profile; empty for Default Settings</param>
        /// <returns>An object for the page: family, points</returns>
        public static object FontFromProfile(string profile)
        {
            string family = PuttyDataHelper.GetProfileValue(profile, "Font");
            int points;
            if (!int.TryParse(PuttyDataHelper.GetProfileValue(profile, "FontHeight"), out points) || points <= 0)
                points = 10;
            return new { family = string.IsNullOrWhiteSpace(family) ? "Courier New" : family, points };
        }

        /// <summary>The installed fonts whose letters are all the same width, for the Font menu; found once</summary>
        private static List<string> monospaceFonts;

        /// <summary>
        /// The installed monospaced fonts (an "i" as wide as a "W"), for the terminal's Font menu and Tools -> Options ->
        /// Terminal. Variable-width fonts are left out: the terminal lays characters out on a grid.
        /// </summary>
        /// <returns>The font names, sorted</returns>
        public static List<string> MonospaceFonts()
        {
            if (monospaceFonts != null)
                return monospaceFonts;
            List<string> found = new List<string>();
            using (System.Drawing.Text.InstalledFontCollection installed = new System.Drawing.Text.InstalledFontCollection())
            {
                foreach (System.Drawing.FontFamily family in installed.Families)
                {
                    if (!family.IsStyleAvailable(System.Drawing.FontStyle.Regular) || family.Name.StartsWith("@", StringComparison.Ordinal))
                        continue;
                    try
                    {
                        using (System.Drawing.Font font = new System.Drawing.Font(family, 12))
                        {
                            TextFormatFlags flags = TextFormatFlags.NoPadding;
                            int narrow = TextRenderer.MeasureText("iiiiiiiiii", font, System.Drawing.Size.Empty, flags).Width;
                            int wide = TextRenderer.MeasureText("WWWWWWWWWW", font, System.Drawing.Size.Empty, flags).Width;
                            if (narrow == wide)
                            {
                                found.Add(family.Name);
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        // a font that can't be created in this style
                    }
                }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            monospaceFonts = found;
            return found;
        }

        /// <summary>
        /// The profile's bell over-use settings (Terminal > Bell): whether it applies, how many bells in how long
        /// silence the bell, and how long a silence brings it back. Missing values get PuTTY's defaults: on, 5 bells
        /// in 2 seconds, 5 seconds.
        /// </summary>
        /// <param name="profile">The PuTTY profile; empty for Default Settings</param>
        /// <returns>An object for the page: on, count, windowMs, quietMs</returns>
        public static object BellOverload(string profile)
        {
            Func<string, int, int> read = (name, fallback) =>
            {
                int value;
                return int.TryParse(PuttyDataHelper.GetProfileValue(profile, name), out value) ? value : fallback;
            };
            return new
            {
                on = read("BellOverload", 1) != 0,
                count = read("BellOverloadN", 5),
                windowMs = read("BellOverloadT", 2000),
                quietMs = read("BellOverloadS", 5000)
            };
        }

        /// <summary>The page knows its size: start the session the first time, then pass changes on to plink</summary>
        /// <param name="cols">Width in characters</param>
        /// <param name="rowCount">Height in lines</param>
        private void OnSize(int cols, int rowCount)
        {
            this.columns = cols;
            this.rows = rowCount;
            if (!this.pageReady)
            {
                this.pageReady = true;
                this.Connect();
            }
            else
            {
                this.pty?.Resize(cols, rowCount);
            }
        }

        /// <summary>Start plink for the session; shows the reason in the terminal when it can't</summary>
        private void Connect()
        {
            if (IsLocalShell(this.session.Proto))
            {
                this.StartLocalShell();
                return;
            }
            string plink = JumpHosts.PlinkPath();
            if (plink == null)
            {
                this.Notice("Neither plink.exe nor klink.exe was found: choose one in Tools > Options > Paths, or put it beside PuTTY. Press Enter to try again.", true);
                this.SetDisconnected();
                return;
            }
            try
            {
                ConPty started = new ConPty();
                started.Output += this.OnOutput;
                started.Exited += code => this.BeginInvokeSafe(() => this.OnExited(started, code));
                // the command line holds the password when -pw is allowed: log the masked one
                Log.InfoFormat("Terminal: {0}", PuttyStartInfo.PlinkCommandLine(this.session, plink, false));
                // in its own folder: a portable KiTTY's klink reads kitty.ini and the Sessions folder from the current
                // directory, not from beside itself, so started anywhere else -load finds no profile (no username, ...)
                started.Start(PuttyStartInfo.PlinkCommandLine(this.session, plink, true), this.columns, this.rows, Path.GetDirectoryName(plink));
                this.pty = started;
                this.startedAt = DateTime.Now;
                this.disconnected = false;
                this.StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error("Could not start plink for " + this.session.SessionId, ex);
                this.Notice("Could not start plink: " + ex.Message + ". Press Enter to try again.", true);
                this.SetDisconnected();
            }
        }

        /// <summary>
        /// Start cmd, PowerShell or WSL directly in the pseudo console, in the folder their sessions start in
        /// (PuttyStartInfo), as Windows Terminal runs them. They read console keys themselves, so unlike plink they need
        /// nothing done to the console mode.
        /// </summary>
        private void StartLocalShell()
        {
            string shell = PuttyStartInfo.GetExecutable(this.session);
            string folder = Environment.ExpandEnvironmentVariables("%userprofile%");
            try
            {
                ConPty started = new ConPty();
                started.Output += this.OnOutput;
                started.Exited += code => this.BeginInvokeSafe(() => this.OnExited(started, code));
                Log.InfoFormat("Terminal: {0} in {1}", shell, folder);
                started.Start("\"" + shell + "\"", this.columns, this.rows, Directory.Exists(folder) ? folder : null);
                this.pty = started;
                this.startedAt = DateTime.Now;
                this.disconnected = false;
                this.StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error("Could not start " + shell + " for " + this.session.SessionId, ex);
                this.Notice("Could not start " + Path.GetFileName(shell) + ": " + ex.Message, true);
            }
        }

        /// <summary>Output from plink, on its reader thread: queue it for the page</summary>
        /// <param name="bytes">The output</param>
        private void OnOutput(byte[] bytes)
        {
            lock (this.pending)
            {
                if (this.pending.Length == 0)
                {
                    this.pendingSince = DateTime.UtcNow;
                }
                this.pending.Write(bytes, 0, bytes.Length);
            }
            this.BeginInvokeSafe(this.SendPending);
        }

        /// <summary>
        /// Log output that waited too long, so a prompt that shows late can be traced to SuperPuTTY (queued here, or
        /// the page slow to draw it) or to the program (it never came). Only times and sizes, never the text.
        /// </summary>
        /// <param name="format">The message, with {0} for the milliseconds and {1} for the bytes</param>
        /// <param name="since">When the wait began (UTC)</param>
        /// <param name="bytes">How much output waited</param>
        private void Delay(string format, DateTime since, int bytes)
        {
            TimeSpan waited = DateTime.UtcNow - since;
            if (waited > SlowOutput)
            {
                Log.InfoFormat(format, (int)waited.TotalMilliseconds, bytes);
            }
        }

        /// <summary>
        /// How much longer queued output should wait before going to the page: output that ends in a line break waits
        /// up to <see cref="LineBreakHold"/> for what follows, so a new prompt is drawn together with the line break
        /// before it rather than one frame later. Anything else (typed characters echoed back) goes at once.
        /// </summary>
        /// <param name="output">The queued output (only the first <paramref name="length"/> bytes count)</param>
        /// <param name="length">How many bytes are queued</param>
        /// <param name="waited">How long the oldest queued output has waited</param>
        /// <returns>The time left to wait; zero to send now</returns>
        public static TimeSpan HoldForNextLine(byte[] output, int length, TimeSpan waited)
        {
            if (waited >= LineBreakHold || !EndsWithLineBreak(output, length))
                return TimeSpan.Zero;
            return LineBreakHold - waited;
        }

        /// <summary>
        /// Whether output ends in a line break (CR or LF; over SSH the two sometimes arrive apart), possibly followed by
        /// escape sequences that don't print anything: bash sends Enter's line break between its bracketed-paste
        /// switches (ESC [ ? 2004 l, CR LF, ESC [ ? 2004 h) and the prompt after them.
        /// </summary>
        /// <param name="output">The output (only the first <paramref name="length"/> bytes count)</param>
        /// <param name="length">How many bytes there are</param>
        /// <returns>true if only CSI or OSC sequences follow the last CR or LF</returns>
        public static bool EndsWithLineBreak(byte[] output, int length)
        {
            // escape sequences after a line break are short; don't scan large output
            int start = Math.Max(0, length - 64);
            int lineBreak = -1;
            for (int i = length - 1; i >= start; i--)
            {
                if (output[i] == (byte)'\n' || output[i] == (byte)'\r')
                {
                    lineBreak = i;
                    break;
                }
            }
            if (lineBreak < 0)
                return false;
            int at = lineBreak + 1;
            while (at < length)
            {
                if (output[at] != 0x1B || at + 1 >= length)
                    return false;
                if (output[at + 1] == (byte)'[')
                {
                    // CSI: parameter and intermediate bytes, then one final byte
                    at += 2;
                    while (at < length && output[at] >= 0x20 && output[at] <= 0x3F)
                        at++;
                    if (at >= length || output[at] < 0x40 || output[at] > 0x7E)
                        return false;
                    at++;
                }
                else if (output[at + 1] == (byte)']')
                {
                    // OSC (a window title): ends with BEL or ESC \
                    at += 2;
                    while (at < length && output[at] != 0x07 && output[at] != 0x1B)
                        at++;
                    if (at >= length || (output[at] == 0x1B && (at + 1 >= length || output[at + 1] != (byte)'\\')))
                        return false;
                    at += output[at] == 0x07 ? 1 : 2;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Send queued output to the page, one chunk at a time</summary>
        private void SendPending()
        {
            if (!this.pageReady || this.waitingForAck)
                return;
            byte[] chunk;
            DateTime queuedSince;
            TimeSpan hold;
            lock (this.pending)
            {
                if (this.pending.Length == 0)
                    return;
                queuedSince = this.pendingSince;
                hold = HoldForNextLine(this.pending.GetBuffer(), (int)this.pending.Length, DateTime.UtcNow - queuedSince);
                if (hold > TimeSpan.Zero)
                {
                    chunk = null;
                }
                else
                {
                    byte[] all = this.pending.ToArray();
                    int take = Math.Min(all.Length, ChunkBytes);
                    chunk = new byte[take];
                    Buffer.BlockCopy(all, 0, chunk, 0, take);
                    this.pending.SetLength(0);
                    this.pending.Write(all, take, all.Length - take);
                    // what's left waits for the page by design, not because it was held up here
                    this.pendingSince = DateTime.UtcNow;
                }
            }
            if (chunk == null)
            {
                // more output (the prompt) calls this again and goes with it; otherwise the timer sends it
                if (!this.lineBreakTimer.Enabled)
                {
                    this.lineBreakTimer.Interval = Math.Max(1, (int)Math.Ceiling(hold.TotalMilliseconds));
                    this.lineBreakTimer.Start();
                }
                return;
            }
            this.Delay("Output waited {0} ms in SuperPuTTY before going to the page ({1} bytes)", queuedSince, chunk.Length);
            this.waitingForAck = true;
            this.chunkSentAt = DateTime.UtcNow;
            this.chunkBytes = chunk.Length;
            this.Post(new { type = "data", data = Convert.ToBase64String(chunk) });
        }

        /// <summary>
        /// plink ended. A clean exit (the user logged out) closes the tab, as PuTTY's does; anything else keeps the tab
        /// with its scrollback and offers to reconnect, automatically when Tools -> Options -> Advanced says so.
        /// </summary>
        /// <param name="ended">The pseudo console that ended (ignored if it's no longer the current one)</param>
        /// <param name="code">plink's exit code</param>
        private void OnExited(ConPty ended, int code)
        {
            if (this.closing || ended != this.pty)
                return;
            this.pty = null;
            ended.Dispose();
            // a local shell ending (exit, or exit 1) closes its tab, as its own window would; there's nothing to reconnect
            if (code == 0 || IsLocalShell(this.session.Proto))
            {
                this.Ended?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (DateTime.Now - this.startedAt > StableAfter)
            {
                this.reconnectAttempts = 0;
            }
            this.SetDisconnected();
            int max = Math.Max(1, SuperPuTTY.Settings.AutoReconnectAttempts);
            int delay = Math.Max(1, SuperPuTTY.Settings.AutoReconnectDelaySeconds);
            if (SuperPuTTY.Settings.AutoReconnect && this.reconnectAttempts < max)
            {
                this.Notice(string.Format("Session ended. Reconnecting in {0} s ({1} of {2}); press Enter to reconnect now.", delay, this.reconnectAttempts + 1, max), false);
                this.reconnectTimer.Interval = delay * 1000;
                this.reconnectTimer.Start();
            }
            else
            {
                this.Notice("Session ended. Press Enter to reconnect.", false);
            }
            SuperPuTTY.ReportStatus($"{this.session.SessionName} disconnected");
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Mark the session as down</summary>
        private void SetDisconnected()
        {
            this.disconnected = true;
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Connect again in the same tab, keeping the scrollback</summary>
        public void Reconnect()
        {
            this.reconnectTimer.Stop();
            if (this.pty != null)
            {
                ConPty old = this.pty;
                this.pty = null;
                old.Dispose();
            }
            this.reconnectAttempts++;
            this.Notice("Reconnecting...", false);
            this.Connect();
        }

        /// <summary>Cancel a scheduled automatic reconnect; Enter still reconnects</summary>
        public void StopReconnecting()
        {
            this.reconnectTimer.Stop();
            this.Notice("Stopped reconnecting. Press Enter to reconnect.", false);
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Show a message from SuperPuTTY in the terminal (it's not sent to the session)</summary>
        /// <param name="text">The message</param>
        /// <param name="error">true to show it in red</param>
        private void Notice(string text, bool error)
        {
            this.Post(new { type = "notice", text, kind = error ? "error" : "info" });
        }

        /// <summary>Typing from the page: to plink, or Enter reconnects while the session is down</summary>
        /// <param name="data">Text and VT key sequences</param>
        private void OnInput(string data)
        {
            if (string.IsNullOrEmpty(data))
                return;
            if (this.disconnected || this.pty == null)
            {
                if (data.IndexOf('\r') >= 0)
                {
                    this.Reconnect();
                }
                return;
            }
            this.Type(this.pty, data);
        }

        /// <summary>
        /// Write typing to plink. Arrow keys and other keys sent as ESC sequences, and Ctrl+C, only reach the device
        /// once plink's console passes them through (ConPty.EnsureVirtualTerminalInput)
        /// </summary>
        /// <param name="target">plink's pseudo console</param>
        /// <param name="data">Text and key sequences</param>
        private void Type(ConPty target, string data)
        {
            // only plink: local shells read keys themselves, and need Ctrl+C as the console signal (to stop ping -t)
            if (!IsLocalShell(this.session.Proto) && NeedsVirtualTerminalInput(data))
            {
                target.EnsureVirtualTerminalInput();
            }
            target.Write(Encoding.UTF8.GetBytes(data));
            // a local shell's running command (ping -t) only stops on the console's Ctrl+C signal, which typing 0x03
            // doesn't raise in a pseudo console; the 0x03 still goes first, for line editors that read it as a key
            if (IsLocalShell(this.session.Proto) && data.IndexOf('\x03') >= 0)
            {
                target.SendCtrlC();
            }
        }

        /// <summary>
        /// Whether text typed into plink needs the console's virtual terminal input (<see cref="ConPty.EnsureVirtualTerminalInput"/>):
        /// it does for ESC sequences, Ctrl+C and every other control character but Enter, Tab and Backspace. Only ESC
        /// and Ctrl+C used to count, and Ctrl+Z (0x1A) then reached plink as the end of its input, so nothing typed
        /// after it was sent.
        /// </summary>
        /// <param name="data">The text about to be typed</param>
        /// <returns>true if it has such a character</returns>
        public static bool NeedsVirtualTerminalInput(string data)
        {
            foreach (char c in data)
            {
                if (c < ' ' && c != '\r' && c != '\t' && c != '\b')
                    return true;
            }
            return false;
        }

        /// <summary>Lines the page has finished showing: append them to the session log and the WAITFOR text</summary>
        /// <param name="lines">Finished lines; may be null or empty</param>
        /// <param name="current">The cursor's line as it is now</param>
        private void OnLog(JArray lines, string current)
        {
            StringBuilder text = new StringBuilder();
            if (lines != null)
            {
                foreach (JToken line in lines)
                {
                    text.Append((string)line).Append('\n');
                }
            }
            lock (this.outputLock)
            {
                if (text.Length > 0)
                {
                    this.recent.Append(text.ToString());
                    this.committedTotal += text.Length;
                    if (this.recent.Length > RecentOutputLimit)
                    {
                        int drop = this.recent.Length - RecentOutputLimit / 2;
                        this.recent.Remove(0, drop);
                        this.recentStart += drop;
                    }
                }
                this.currentLine = current ?? string.Empty;
            }
            if (text.Length > 0 && this.log != null)
            {
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes((this.LogStamp(DateTime.Now) + text).Replace("\n", "\r\n"));
                    this.log.Write(bytes, 0, bytes.Length);
                    this.log.Flush();
                }
                catch (IOException ex)
                {
                    Log.Warn("Could not write the session log " + this.logPath, ex);
                }
            }
        }

        /// <summary>
        /// A time stamp line for the session log, before lines written now, when the logTimestamps preference's
        /// interval (30 seconds by default) has passed since the last one. Stamps come only with output, so an idle
        /// session adds none, and a burst of output gets one; they go to the log file, not to WAITFOR's text.
        /// </summary>
        /// <param name="now">The time the lines are written</param>
        /// <returns>"--- 2026-11-01 02:50:12 ---" and a line break, or empty</returns>
        private string LogStamp(DateTime now)
        {
            string stamp = TimeStamp(TerminalPreferences.Get("logTimestamps"), this.lastLogStamp, now);
            if (stamp.Length > 0)
            {
                this.lastLogStamp = now;
            }
            return stamp;
        }

        /// <summary>The time stamp line due now, if any (<see cref="LogStamp"/>)</summary>
        /// <param name="seconds">The logTimestamps preference: seconds between stamps; "0" or anything not a positive number for none</param>
        /// <param name="last">When the last stamp was written; DateTime.MinValue for none yet</param>
        /// <param name="now">Now</param>
        /// <returns>The stamp and a line break, or empty when none is due</returns>
        public static string TimeStamp(string seconds, DateTime last, DateTime now)
        {
            int interval;
            if (!int.TryParse(seconds, out interval) || interval <= 0 || now - last < TimeSpan.FromSeconds(interval))
                return string.Empty;
            return "--- " + now.ToString("yyyy-MM-dd HH:mm:ss") + " ---\n";
        }

        /// <summary>
        /// Make WAITFOR look only at output after this point: from now (after a command is sent), or from the start of
        /// the cursor's line (so a prompt already showing counts)
        /// </summary>
        /// <param name="atLineStart">true to include the cursor's line</param>
        public void MarkOutput(bool atLineStart)
        {
            lock (this.outputLock)
            {
                this.mark = this.committedTotal + (atLineStart ? 0 : this.currentLine.Length);
            }
        }

        /// <summary>The output since the WAITFOR mark: logged lines, then the cursor's line</summary>
        /// <returns>The text, "\n" between lines</returns>
        public string OutputSinceMark()
        {
            lock (this.outputLock)
            {
                if (this.mark >= this.committedTotal)
                {
                    int skip = (int)Math.Min(this.currentLine.Length, this.mark - this.committedTotal);
                    return this.currentLine.Substring(skip);
                }
                int from = (int)Math.Max(0, this.mark - this.recentStart);
                return this.recent.ToString(from, this.recent.Length - from) + this.currentLine;
            }
        }

        /// <summary>Move the WAITFOR mark forward past a match</summary>
        /// <param name="characters">How far, counted in the text <see cref="OutputSinceMark"/> returned</param>
        public void AdvanceMark(int characters)
        {
            lock (this.outputLock)
            {
                this.mark = Math.Max(this.mark, this.recentStart) + characters;
            }
        }

        /// <summary>Run one of the page's menu actions from the tab menu: clearScrollback, reset, copyAll</summary>
        /// <param name="action">The action's name</param>
        public void RunAction(string action)
        {
            this.Post(new { type = "action", name = action });
        }

        /// <summary>Put text on the clipboard; another program may hold it for a moment</summary>
        /// <param name="text">The text; nothing happens when it's empty</param>
        private void SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                Log.Warn("Could not copy to the clipboard", ex);
            }
        }

        /// <summary>
        /// Type a command's text and keys into the session (Send Commands, the button bar, SPSL), then wait for its
        /// delay. Safe to call from any thread; nothing is typed while the session is down.
        /// </summary>
        /// <param name="command">The text, keys and delay</param>
        public void SendCommand(CommandData command)
        {
            ConPty target = this.pty;
            if (target != null && !this.disconnected)
            {
                StringBuilder text = new StringBuilder(LineFeedsAsEnter(command.Command));
                if (command.KeyData != null)
                {
                    string key = KeySequence(command.KeyData);
                    for (int press = 0; press < command.KeyRepeat; press++)
                    {
                        text.Append(key);
                    }
                }
                this.Type(target, text.ToString());
            }
            if (command.Delay > TimeSpan.Zero)
            {
                System.Threading.Thread.Sleep(command.Delay);
            }
        }

        /// <summary>
        /// Turn each line feed (a button's \n, or a line break in a Send Commands string) into Enter (CR). With PuTTY a
        /// line feed reached the device as a character, which shells and switches take as Enter; typed into a pseudo
        /// console it becomes Ctrl+Enter, which plink and cmd drop, so the command was typed but never run. A CR LF pair
        /// becomes one Enter.
        /// </summary>
        /// <param name="text">Text to type; may be null</param>
        /// <returns>The text with CR in place of every line feed; empty for null</returns>
        public static string LineFeedsAsEnter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Replace("\r\n", "\r").Replace('\n', '\r');
        }

        /// <summary>
        /// What a terminal sends for a key (xterm's sequences, which plink passes on): Enter is CR, arrows are ESC [ A..D,
        /// Ctrl+letter or Ctrl+] is its control character, a character key is its character, Alt puts ESC in front.
        /// The mapping is <see cref="TerminalKeys.Sequence"/>, shared with PuTTY windows.
        /// </summary>
        /// <param name="key">The key and modifiers</param>
        /// <returns>The characters to send; empty for keys with none</returns>
        public static string KeySequence(KeyEventArgs key)
        {
            return TerminalKeys.Sequence(key);
        }

        /// <summary>Run on the UI thread later, unless the control is gone</summary>
        /// <param name="action">What to run</param>
        private void BeginInvokeSafe(Action action)
        {
            try
            {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke(action);
                }
            }
            catch (InvalidOperationException)
            {
                // closed between the check and the call
            }
        }

        /// <summary>End plink, close the log and release the browser when the tab closes</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.closing = true;
                TerminalPreferences.Changed -= this.OnPreferencesChanged;
                this.reconnectTimer.Dispose();
                this.lineBreakTimer.Dispose();
                this.pty?.Dispose();
                this.pty = null;
                this.log?.Dispose();
                this.log = null;
                this.webView.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
