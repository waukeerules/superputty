using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DarkModeForms;
using log4net;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Microsoft's Remote Desktop ActiveX control (mstscax.dll, MsRdpClient9NotSafeForScripting) hosted directly in
    /// an RDP tab, the way Remote Desktop Connection Manager (RDCMan) does it, instead of starting mstsc.exe and
    /// capturing its window. The remote desktop is sized to the tab when connecting; when the tab is resized the
    /// remote resolution follows (UpdateSessionDisplaySettings, Windows 8.1 / Server 2012 R2 and later), and older
    /// remotes are scaled to fit (SmartSizing).
    /// <para>
    /// The control is late bound through IDispatch (<c>dynamic</c>), so no generated interop assemblies are needed;
    /// only the few events used are declared, in <see cref="IMsTscAxEvents"/>. Created by ctlPuttyPanel when
    /// <see cref="ShouldEmbed"/> is true, and hosted by <see cref="ApplicationPanel"/> as an <see cref="IEmbeddedApplication"/>.
    /// </para>
    /// </summary>
    [System.ComponentModel.ToolboxItem(false)]
    public class RdpControl : AxHost, IEmbeddedApplication, ICommandReceiver
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(RdpControl));

        /// <summary>CLSID of MsRdpClient9NotSafeForScripting, the first version with UpdateSessionDisplaySettings; "not safe for scripting" lets a stored password be used</summary>
        private const string ClsidMsRdpClient9NotSafeForScripting = "8b918b82-7985-4c24-89df-c33ad2bbfbcd";

        /// <summary>Smallest desktop size the control accepts</summary>
        private const int MinDesktopSize = 200;

        /// <summary>Largest desktop size the control accepts</summary>
        private const int MaxDesktopSize = 8192;

        /// <summary>How long the tab size must stay unchanged before the remote resolution is updated, so dragging doesn't flood the server</summary>
        private const int ResizeDelayMs = 500;

        /// <summary>The session being connected to, with {key} placeholders already replaced</summary>
        private readonly SessionData session;

        /// <summary>The options to connect with: Tools -> Options -> RDP with this session's overrides, fixed when the tab opens</summary>
        private readonly RdpOptions options;

        /// <summary>Waits for resizing to settle before updating the remote resolution</summary>
        private readonly Timer resizeTimer;

        /// <summary>The ActiveX control, late bound (IMsRdpClient9)</summary>
        private dynamic client;

        /// <summary>Connection to the control's events</summary>
        private ConnectionPointCookie eventsCookie;

        /// <summary>True after login completes, when the remote resolution can be changed</summary>
        private bool loggedIn;

        /// <summary>True once the remote has refused a resolution change; from then on smart sizing scales instead</summary>
        private bool dynamicResizeUnsupported;

        /// <summary>True while the tab is closing, so the disconnect it causes doesn't raise <see cref="Ended"/></summary>
        private bool closing;

        /// <summary>
        /// A panel in the theme's colour over the control until the connection is up and after it ends: the control
        /// paints itself white then, and has no setting for that colour (see <see cref="ShowCover"/>). After the session
        /// ends it says why, and a click on it connects again.
        /// </summary>
        private Label cover;

        /// <summary>True from the session ending until it's connecting again: the tab stays, showing why it ended</summary>
        private bool ended;

        /// <summary>True when the tab menu's Disconnect ended the session, so it isn't taken for a drop</summary>
        private bool userDisconnected;

        /// <summary>Automatic reconnects tried since the session last dropped (Options > Advanced > Dropped sessions)</summary>
        private int reconnectAttempts;

        /// <summary>Counts down to the next automatic reconnect, a tick a second; stopped when none is due</summary>
        private readonly Timer reconnectTimer;

        /// <summary>Seconds left before the next automatic reconnect</summary>
        private int reconnectSecondsLeft;

        /// <summary>The first line of what the cover says after the session ended, kept while the countdown runs</summary>
        private string endReason;

        /// <summary>Create the control for a session; nothing connects until <see cref="Start"/></summary>
        /// <param name="session">The session, with {key} placeholders already replaced</param>
        public RdpControl(SessionData session) : base(ClsidMsRdpClient9NotSafeForScripting)
        {
            this.session = session;
            this.options = RdpOptions.EffectiveFor(session);
            this.resizeTimer = new Timer { Interval = ResizeDelayMs };
            this.resizeTimer.Tick += this.resizeTimer_Tick;
            this.reconnectTimer = new Timer { Interval = 1000 };
            this.reconnectTimer.Tick += this.reconnectTimer_Tick;
        }

        /// <summary>
        /// Never raised: an RDP tab stays open when its session ends, showing why, with a click to connect again and,
        /// for a drop, a countdown to reconnecting, as SuperXTerminal's (<see cref="StateChanged"/> says so instead).
        /// It used to close the tab, after a message box for anything but a plain logoff.
        /// </summary>
        public event EventHandler Ended
        {
            add { }
            remove { }
        }

        /// <summary>
        /// Raised when the session ends, starts connecting again or is back, so the tab can show the disconnected badge
        /// and the Sessions tree its state (ctlPuttyPanel), as for the built-in terminal
        /// </summary>
        public event EventHandler StateChanged;

        /// <summary>Whether the session is up or on its way (not ended)</summary>
        public bool IsConnected
        {
            get { return !this.ended; }
        }

        /// <summary>Whether it's on its way: connecting until the server answers (<see cref="OnConnected"/>)</summary>
        public bool IsConnecting
        {
            get { return !this.ended && this.connecting; }
        }

        /// <summary>Set from <see cref="Start"/> or a reconnect until the server answers or the session ends</summary>
        private bool connecting;

        /// <summary>Whether an automatic reconnect is counting down (the tab menu's Stop Reconnecting)</summary>
        public bool ReconnectScheduled
        {
            get { return this.reconnectTimer.Enabled; }
        }

        /// <summary>What happens after an RDP session ends (<see cref="AfterEnd"/>)</summary>
        public enum EndAction
        {
            /// <summary>Nothing by itself: the tab says why it ended and waits for a click to connect again</summary>
            WaitForClick,
            /// <summary>Count down and connect again</summary>
            Reconnect,
            /// <summary>Gave up reconnecting after the last attempt: waits for a click</summary>
            GaveUp
        }

        /// <summary>
        /// What to do when a session ends, as SuperXTerminal's RdpHost: only a drop (not a logoff or a disconnect from
        /// either side) of a session that had reached the desktop, or a reconnect that failed, is tried again
        /// automatically, with Options > Advanced > Dropped sessions on, up to its number of attempts. A login that
        /// failed isn't, so a wrong password isn't retried into an account lockout.
        /// </summary>
        /// <param name="clean">The session ended normally (<see cref="ShouldReport"/> false)</param>
        /// <param name="wasLoggedIn">It had reached the desktop</param>
        /// <param name="attempts">Automatic reconnects tried since it dropped</param>
        /// <param name="autoReconnect">Options: reconnect dropped sessions</param>
        /// <param name="maxAttempts">Options: how many times</param>
        /// <returns>What to do</returns>
        public static EndAction AfterEnd(bool clean, bool wasLoggedIn, int attempts, bool autoReconnect, int maxAttempts)
        {
            if (clean || !autoReconnect || (!wasLoggedIn && attempts == 0))
                return EndAction.WaitForClick;
            return attempts < Math.Max(1, maxAttempts) ? EndAction.Reconnect : EndAction.GaveUp;
        }

        /// <summary>
        /// Whether RDP sessions should use this control: the "Built-in client" option is on, the configured RDP
        /// client isn't FreeRDP (which keeps being started as a program), and the control is installed
        /// </summary>
        /// <param name="rdpExecutable">The configured RDP client (Settings.RDPExe)</param>
        /// <returns>true to host RDP sessions in this control</returns>
        public static bool ShouldEmbed(string rdpExecutable)
        {
            if (!SuperPuTTY.Settings.RdpUseEmbeddedClient
                || (rdpExecutable ?? string.Empty).EndsWith("wfreerdp.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\{" + ClsidMsRdpClient9NotSafeForScripting + "}"))
            {
                if (key == null)
                {
                    Log.Warn("The Remote Desktop ActiveX control (MsRdpClient9) is not installed; starting the RDP client program instead");
                }
                return key != null;
            }
        }

        /// <summary>Keep a reference to the ActiveX control once AxHost has created it</summary>
        protected override void AttachInterfaces()
        {
            this.client = this.GetOcx();
        }

        /// <summary>Subscribe to the control's events</summary>
        protected override void CreateSink()
        {
            try
            {
                this.eventsCookie = new ConnectionPointCookie(this.GetOcx(), new EventSink(this), typeof(IMsTscAxEvents));
            }
            catch (Exception ex)
            {
                Log.Error("Could not subscribe to the Remote Desktop control's events", ex);
            }
        }

        /// <summary>Unsubscribe from the control's events</summary>
        protected override void DetachSink()
        {
            if (this.eventsCookie != null)
            {
                this.eventsCookie.Disconnect();
                this.eventsCookie = null;
            }
        }

        /// <summary>
        /// Configure the connection and connect, with the remote desktop sized to the control. Windows asks for
        /// credentials (CredSSP) unless the session has a password.
        /// </summary>
        public void Start()
        {
            this.CreateControl();
            this.ShowCover("Connecting to " + this.session.SessionName + "...");
            Size size = this.FitsTab
                ? ClampDesktopSize(this.ClientSize)
                : ClampDesktopSize(new Size(this.options.FixedWidth.Value, this.options.FixedHeight.Value));

            this.client.Server = this.session.Host;
            if (this.session.Port != 0)
            {
                this.client.AdvancedSettings2.RDPPort = this.session.Port;
            }
            if (!string.IsNullOrEmpty(this.session.Username))
            {
                this.client.UserName = this.session.Username;
            }
            this.client.DesktopWidth = size.Width;
            this.client.DesktopHeight = size.Height;
            this.client.ColorDepth = this.options.ColorDepth.Value;

            // Tools -> Options -> RDP, with this session's overrides (RDP options... in Edit Session)
            dynamic settings = this.client.AdvancedSettings9;
            settings.SmartSizing = this.options.SmartSizing.Value;
            settings.EnableCredSspSupport = true;
            settings.AuthenticationLevel = (uint)this.options.AuthenticationLevel.Value;
            settings.RedirectClipboard = this.options.RedirectClipboard.Value;
            settings.RedirectDrives = this.options.RedirectDrives.Value;
            settings.RedirectPrinters = this.options.RedirectPrinters.Value;
            settings.RedirectSmartCards = this.options.RedirectSmartCards.Value;
            settings.AudioCaptureRedirectionMode = this.options.RedirectMicrophone.Value;
            settings.AudioRedirectionMode = (uint)this.options.AudioMode.Value;
            settings.ConnectToAdministerServer = this.options.ConnectToConsole.Value;
            // ride out network drops with the control's own "reconnecting" overlay (Options -> Advanced -> Dropped sessions)
            settings.EnableAutoReconnect = SuperPuTTY.Settings.AutoReconnect;
            this.client.SecuredSettings3.KeyboardHookMode = this.options.KeyboardHookMode.Value;
            if (!string.IsNullOrWhiteSpace(this.options.GatewayHost))
            {
                dynamic transport = this.client.TransportSettings4;
                transport.GatewayHostname = this.options.GatewayHost.Trim();
                // use the gateway for this connection, with credentials negotiated as needed
                transport.GatewayUsageMethod = 1u;
                transport.GatewayProfileUsageMethod = 1u;
                transport.GatewayCredsSource = 4u;
            }
            if (!string.IsNullOrEmpty(this.session.Password))
            {
                settings.ClearTextPassword = this.session.Password;
            }
            // the display's scale, so at 150 % the remote's text isn't drawn at 100 %, tiny (as SuperXTerminal's and mstsc)
            this.ApplyScaleFactors();

            this.connecting = true;
            this.StateChanged?.Invoke(this, EventArgs.Empty);
            Log.InfoFormat("Connecting to {0}:{1} as '{2}' with a {3}x{4} desktop{5}", this.session.Host, this.session.Port, this.session.Username, size.Width, size.Height,
                string.IsNullOrWhiteSpace(this.options.GatewayHost) ? "" : " through gateway " + this.options.GatewayHost);
            this.client.Connect();
        }

        /// <summary>
        /// Connect again after the session ended: from the tab menu's Reconnect, a click on the tab, or the countdown
        /// after a drop. The settings from <see cref="Start"/> still hold; the desktop is sized to the tab as it is now.
        /// </summary>
        public void Reconnect()
        {
            this.Reconnect(false);
        }

        /// <summary>Connect again; a manual reconnect starts the attempts afresh</summary>
        /// <param name="automatic">True for the countdown's reconnect</param>
        private void Reconnect(bool automatic)
        {
            if (this.IsDisposed || this.client == null || !this.ended)
                return;
            this.reconnectTimer.Stop();
            if (!automatic)
            {
                this.reconnectAttempts = 0;
            }
            this.ended = false;
            this.connecting = true;
            this.userDisconnected = false;
            this.ShowCover(automatic
                ? string.Format("Reconnecting to {0} (try {1} of {2})...", this.session.SessionName, this.reconnectAttempts, Math.Max(1, SuperPuTTY.Settings.AutoReconnectAttempts))
                : "Connecting to " + this.session.SessionName + "...");
            this.StateChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                if (this.FitsTab)
                {
                    Size size = ClampDesktopSize(this.ClientSize);
                    this.client.DesktopWidth = size.Width;
                    this.client.DesktopHeight = size.Height;
                }
                Log.InfoFormat("Reconnecting to {0}{1}", this.session.Host, automatic ? " (attempt " + this.reconnectAttempts + ")" : "");
                this.client.Connect();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not reconnect to " + this.session.Host, ex);
                this.ended = true;
                this.ShowCover("Could not connect to " + this.session.SessionName + ": " + ex.Message + "\n\nClick to try again.");
                this.StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Disconnect from the tab menu; the tab stays, and a click on it connects again</summary>
        public void Disconnect()
        {
            if (this.ended || this.client == null)
                return;
            this.userDisconnected = true;
            try
            {
                this.client.Disconnect();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not disconnect from " + this.session.Host, ex);
            }
        }

        /// <summary>Stop the countdown to the next automatic reconnect (the tab menu's Stop Reconnecting)</summary>
        public void StopReconnecting()
        {
            if (!this.reconnectTimer.Enabled)
                return;
            this.reconnectTimer.Stop();
            this.ShowCover((this.endReason ?? "Disconnected") + "\n\nStopped reconnecting. Click to connect again.");
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A second of the countdown to reconnecting: say how long is left, and reconnect at zero</summary>
        /// <param name="sender">The timer</param>
        /// <param name="e">Not used</param>
        private void reconnectTimer_Tick(object sender, EventArgs e)
        {
            this.reconnectSecondsLeft--;
            if (this.reconnectSecondsLeft > 0)
            {
                this.ShowReconnectCountdown();
                return;
            }
            this.reconnectTimer.Stop();
            this.reconnectAttempts++;
            this.Reconnect(true);
        }

        /// <summary>Show the countdown on the cover, under why the session ended</summary>
        private void ShowReconnectCountdown()
        {
            this.ShowCover(string.Format("{0}\n\nReconnecting in {1} s (try {2} of {3}). Click to reconnect now.",
                this.endReason, this.reconnectSecondsLeft, this.reconnectAttempts + 1, Math.Max(1, SuperPuTTY.Settings.AutoReconnectAttempts)));
        }

        /// <summary>True when the remote desktop follows the tab's size (the effective Desktop size is "Fit the tab")</summary>
        private bool FitsTab
        {
            get { return this.options.DisplayMode != "Fixed"; }
        }

        /// <summary>Scan code of the left Shift key</summary>
        private const int ScanShift = 0x2A;

        /// <summary>Scan code of the left Ctrl key</summary>
        private const int ScanCtrl = 0x1D;

        /// <summary>Scan code of the left Alt key</summary>
        private const int ScanAlt = 0x38;

        /// <summary>Flag on a scan code sent to the control for an extended key (arrows, Home, Delete, ...)</summary>
        private const int ExtendedKeyFlag = 0x100;

        /// <summary>Keys whose scan codes are "extended" (prefixed with E0 on a real keyboard)</summary>
        private static readonly System.Collections.Generic.HashSet<Keys> ExtendedKeys = new System.Collections.Generic.HashSet<Keys>
        {
            Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
            Keys.Insert, Keys.Delete, Keys.Divide, Keys.NumLock, Keys.RControlKey, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Apps
        };

        /// <summary>
        /// Type a command into the remote desktop as keystrokes: its text, then its key (e.g. Enter), then wait for
        /// its delay. The keys go through the control's SendKeys as scan codes, mapped with the local keyboard layout,
        /// so the remote should use the same layout. Characters that can't be typed on that layout are skipped.
        /// Does nothing until login completes.
        /// </summary>
        /// <param name="command">The text, keys and delay to send</param>
        public void SendCommand(CommandData command)
        {
            // the control is a COM object living on the UI thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => this.SendKeystrokes(command)));
            }
            else
            {
                this.SendKeystrokes(command);
            }
            if (command.Delay > TimeSpan.Zero)
            {
                System.Threading.Thread.Sleep(command.Delay);
            }
        }

        /// <summary>
        /// Press keys together in the remote desktop, from the tab menu's Send Key (Ctrl+Alt+Del, the Windows key, Alt+Tab:
        /// keys Windows or SuperXPuTTY would take before they reached the control), as SuperXTerminal's RDP tab menu. Sent as
        /// fixed scan codes (<see cref="RdpKeys"/>). Works from the moment the connection is up, the login screen too.
        /// </summary>
        /// <param name="keys">The keys, from <see cref="RdpKeys.MenuKeys"/></param>
        public void SendKeyCombination(Keys[] keys)
        {
            if (this.ended || this.IsDisposed)
                return;
            int[] scanCodes;
            short[] keyUps;
            RdpKeys.Events(keys, out scanCodes, out keyUps);
            try
            {
                ((IMsRdpClientNonScriptable)this.GetOcx()).SendKeys(scanCodes.Length, keyUps, scanCodes);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not send keys to " + this.session.Host, ex);
            }
            this.Focus();
        }

        /// <summary>Whether this session's Windows keys option is "only when full screen" (RdpWindowsKeys passes them on then)</summary>
        public bool WindowsKeysOnlyInFullScreen
        {
            get { return this.options.KeyboardHookMode == 2; }
        }

        /// <summary>
        /// Send one key going down or up, by its scan code (<see cref="RdpKeys.ScanCodeOf"/>): RdpWindowsKeys passes the
        /// Windows key and Alt+Tab on with it while SuperXPuTTY is full screen
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="up">true for the key going up</param>
        public void SendScanCode(Keys key, bool up)
        {
            int code = RdpKeys.ScanCodeOf(key);
            if (code == 0 || this.ended || this.IsDisposed)
                return;
            try
            {
                ((IMsRdpClientNonScriptable)this.GetOcx()).SendKeys(1, new short[] { (short)(up ? -1 : 0) }, new[] { code });
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a key to " + this.session.Host, ex);
            }
        }

        /// <summary>
        /// A picture of the remote desktop as shown in the tab (the tab menu's Copy Screenshot, as SuperXTerminal's): the
        /// control's window drawn with PrintWindow's full-content mode, which takes what the control draws with DirectX too
        /// </summary>
        /// <returns>The picture; the caller disposes of it</returns>
        public System.Drawing.Bitmap Screenshot()
        {
            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(Math.Max(1, this.Width), Math.Max(1, this.Height));
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                try
                {
                    // PW_RENDERFULLCONTENT (2): DirectX and DWM content too, Windows 8.1 and later
                    NativeMethods.PrintWindow(this.Handle, dc, 2);
                }
                finally
                {
                    g.ReleaseHdc(dc);
                }
            }
            return bitmap;
        }

        /// <summary>
        /// Type text into the remote desktop, key by key: the tab menu's Type Clipboard Text, for where pasting doesn't
        /// reach (Windows' login screen, a console without clipboard sharing), as SuperXTerminal's. Line breaks are
        /// typed as Enter. Unlike <see cref="SendCommand"/>, it doesn't wait for the desktop: the login screen comes first.
        /// </summary>
        /// <param name="text">The text</param>
        public void TypeText(string text)
        {
            if (this.ended || this.IsDisposed || string.IsNullOrEmpty(text))
                return;
            this.SendKeystrokes(new CommandData(text.Replace("\r\n", "\r").Replace('\n', '\r')), false);
            this.Focus();
        }

        /// <summary>Convert a command to scan codes and pass them to the control in one SendKeys call, once logged in</summary>
        /// <param name="command">The text and keys to send</param>
        private void SendKeystrokes(CommandData command)
        {
            this.SendKeystrokes(command, true);
        }

        /// <summary>Convert a command to scan codes and pass them to the control in one SendKeys call</summary>
        /// <param name="command">The text and keys to send</param>
        /// <param name="needsDesktop">Only once the session has reached the desktop (scripts); false for the login screen too</param>
        private void SendKeystrokes(CommandData command, bool needsDesktop)
        {
            if ((needsDesktop && !this.loggedIn) || this.ended || this.IsDisposed)
                return;

            System.Collections.Generic.List<int> scanCodes = new System.Collections.Generic.List<int>();
            System.Collections.Generic.List<short> keyUps = new System.Collections.Generic.List<short>();

            foreach (char c in command.Command ?? string.Empty)
            {
                short vkAndShift = NativeMethods.VkKeyScan(c);
                if (vkAndShift == -1)
                {
                    Log.WarnFormat("Can't type U+{0:X4} on the local keyboard layout; skipped", (int)c);
                    continue;
                }
                int shiftState = (vkAndShift >> 8) & 0xFF;
                AddKeyPress((Keys)(vkAndShift & 0xFF), (shiftState & 1) != 0, (shiftState & 2) != 0, (shiftState & 4) != 0, scanCodes, keyUps);
            }
            for (int press = 0; command.KeyData != null && press < command.KeyRepeat; press++)
            {
                AddKeyPress(command.KeyData.KeyCode, command.KeyData.Shift, command.KeyData.Control, command.KeyData.Alt, scanCodes, keyUps);
            }

            if (scanCodes.Count > 0)
            {
                try
                {
                    ((IMsRdpClientNonScriptable)this.GetOcx()).SendKeys(scanCodes.Count, keyUps.ToArray(), scanCodes.ToArray());
                }
                catch (Exception ex)
                {
                    // e.g. E_FAIL while the session is reconnecting; this may run for a background sender, so never throw
                    Log.Warn("Could not send keys to " + this.session.Host, ex);
                }
            }
        }

        /// <summary>Add one key press (down and up), wrapped in the modifier keys it needs</summary>
        /// <param name="key">The virtual key</param>
        /// <param name="shift">Hold Shift</param>
        /// <param name="ctrl">Hold Ctrl</param>
        /// <param name="alt">Hold Alt</param>
        /// <param name="scanCodes">Scan codes to add to</param>
        /// <param name="keyUps">Matching key-up flags to add to (VARIANT_BOOL: -1 up, 0 down)</param>
        private static void AddKeyPress(Keys key, bool shift, bool ctrl, bool alt, System.Collections.Generic.List<int> scanCodes, System.Collections.Generic.List<short> keyUps)
        {
            int scanCode = (int)NativeMethods.MapVirtualKey((uint)key, 0);
            if (scanCode == 0)
            {
                Log.WarnFormat("No scan code for key {0}; skipped", key);
                return;
            }
            if (ExtendedKeys.Contains(key))
            {
                scanCode |= ExtendedKeyFlag;
            }

            if (shift) { scanCodes.Add(ScanShift); keyUps.Add(0); }
            if (ctrl) { scanCodes.Add(ScanCtrl); keyUps.Add(0); }
            if (alt) { scanCodes.Add(ScanAlt); keyUps.Add(0); }
            scanCodes.Add(scanCode); keyUps.Add(0);
            scanCodes.Add(scanCode); keyUps.Add(-1);
            if (alt) { scanCodes.Add(ScanAlt); keyUps.Add(-1); }
            if (ctrl) { scanCodes.Add(ScanCtrl); keyUps.Add(-1); }
            if (shift) { scanCodes.Add(ScanShift); keyUps.Add(-1); }
        }

        /// <summary>Give the remote desktop the keyboard focus</summary>
        public void FocusApplication()
        {
            this.Focus();
        }

        /// <summary>Update the remote resolution once resizing has settled</summary>
        /// <param name="e">Not used</param>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (this.loggedIn && this.FitsTab && !this.dynamicResizeUnsupported)
            {
                this.resizeTimer.Stop();
                this.resizeTimer.Start();
            }
        }

        /// <summary>
        /// Ask the remote to change its resolution to the control's size. Remotes older than Windows 8.1 /
        /// Server 2012 R2 refuse; after that, smart sizing scales the picture instead.
        /// </summary>
        /// <param name="sender">The resize timer</param>
        /// <param name="e">Not used</param>
        private void resizeTimer_Tick(object sender, EventArgs e)
        {
            this.resizeTimer.Stop();
            if (!this.loggedIn || !this.FitsTab || this.dynamicResizeUnsupported)
                return;

            Size size = ClampDesktopSize(this.ClientSize);
            try
            {
                // desktop size, physical size, orientation (landscape), and the display's desktop and device scale
                uint desktopScale, deviceScale;
                ScaleFactors(this.DeviceDpi, out desktopScale, out deviceScale);
                this.client.UpdateSessionDisplaySettings((uint)size.Width, (uint)size.Height, (uint)size.Width, (uint)size.Height, 0u, desktopScale, deviceScale);
                Log.InfoFormat("Changed the remote resolution of {0} to {1}x{2}", this.session.Host, size.Width, size.Height);
            }
            catch (Exception ex)
            {
                this.dynamicResizeUnsupported = true;
                Log.InfoFormat("{0} can't change resolution while connected ({1}); scaling to fit instead", this.session.Host, ex.Message);
            }
        }

        /// <summary>Called when login completes: resizing can now change the remote resolution</summary>
        private void OnLoginComplete()
        {
            this.loggedIn = true;
            this.HideCover();
            Log.InfoFormat("Logged in to {0}", this.session.Host);
            // the tab may have been resized while connecting
            if (ClampDesktopSize(this.ClientSize) != new Size((int)this.client.DesktopWidth, (int)this.client.DesktopHeight))
            {
                this.resizeTimer.Start();
            }
        }

        /// <summary>
        /// Called when the session ends. The tab stays, as SuperXTerminal's: the cover says why (Windows' description of
        /// the error, for anything but a disconnect or logoff), and a click on it connects again; a dropped session
        /// counts down to reconnecting (<see cref="AfterEnd"/>). It used to close the tab, after a message box.
        /// </summary>
        /// <param name="reason">The disconnect reason code</param>
        private void OnDisconnected(int reason)
        {
            bool wasLoggedIn = this.loggedIn;
            this.loggedIn = false;
            if (this.closing)
                return;
            this.resizeTimer.Stop();

            int extended = 0;
            try
            {
                extended = (int)this.client.ExtendedDisconnectReason;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the extended disconnect reason", ex);
            }
            Log.InfoFormat("Disconnected from {0}, reason {1}, extended reason {2}", this.session.Host, reason, extended);
            bool clean = this.userDisconnected || !ShouldReport(reason, extended, wasLoggedIn);
            this.endReason = "Disconnected from " + this.session.SessionName;
            if (!clean)
            {
                string description = "error " + reason;
                try
                {
                    description = this.client.GetErrorDescription((uint)reason, (uint)extended);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not get the disconnect description", ex);
                }
                this.endReason = this.session.SessionName + ": " + description;
            }
            this.ended = true;
            this.connecting = false;
            this.userDisconnected = false;
            switch (AfterEnd(clean, wasLoggedIn, this.reconnectAttempts, SuperPuTTY.Settings.AutoReconnect, SuperPuTTY.Settings.AutoReconnectAttempts))
            {
                case EndAction.Reconnect:
                    this.reconnectSecondsLeft = Math.Max(1, SuperPuTTY.Settings.AutoReconnectDelaySeconds);
                    this.ShowReconnectCountdown();
                    this.reconnectTimer.Start();
                    SuperPuTTY.ReportStatus(this.endReason + ". Reconnecting in " + this.reconnectSecondsLeft + " s");
                    break;
                case EndAction.GaveUp:
                    this.ShowCover(this.endReason + "\n\nGave up reconnecting. Click to try again.");
                    SuperPuTTY.ReportStatus("Gave up reconnecting " + this.session.SessionName);
                    break;
                default:
                    this.ShowCover(this.endReason + (clean ? "." : "") + "\n\nClick to connect again.");
                    break;
            }
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Called when the connection is up, before Windows' own login screen, which must stay visible</summary>
        private void OnConnected()
        {
            this.HideCover();
            // the server answered: connected, not connecting (the tab's and tree's dot go green)
            this.connecting = false;
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Cover the control with a panel in the theme's colour, showing <paramref name="text"/>: before the connection
        /// is up and after it ends, the ActiveX control paints itself white, which glares on the dark theme, and it has
        /// no setting for that colour. The panel is a sibling in front of the control rather than the control hidden,
        /// so the control stays active while it connects.
        /// </summary>
        /// <param name="text">What's happening, e.g. "Connecting to Windows 10 Host..."</param>
        private void ShowCover(string text)
        {
            if (this.Parent == null || this.IsDisposed)
                return;
            if (this.cover == null)
            {
                bool dark = SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;
                this.cover = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = dark ? Color.FromArgb(30, 30, 30) : SystemColors.Control,
                    ForeColor = dark ? Color.FromArgb(150, 150, 150) : SystemColors.GrayText,
                    Font = new Font(this.Font.FontFamily, 11f)
                };
                // after the session ended, a click connects again (or now, during a countdown)
                this.cover.Click += (s, e) => this.Reconnect(false);
                this.Parent.Controls.Add(this.cover);
            }
            this.cover.Text = text;
            this.cover.Cursor = this.ended ? Cursors.Hand : Cursors.Default;
            this.cover.Visible = true;
            this.cover.BringToFront();
        }

        /// <summary>Take the cover away once the remote's screen is showing</summary>
        private void HideCover()
        {
            if (this.cover != null)
            {
                this.cover.Visible = false;
            }
        }

        /// <summary>
        /// Whether a disconnect should be explained before the tab closes. Reasons 1-3 (disconnected here, logged off,
        /// disconnected by the server) were all taken as normal, so a server refusing the login ("the user account is
        /// not authorized for remote login") closed the tab without a word: that comes as reason 3 before the desktop
        /// is reached. Now a session that never logged in, or whose extended reason is the server's (idle or logon
        /// timeout, replaced by another connection, out of memory, denied, insufficient privileges, fresh credentials
        /// required, licensing or protocol errors), is explained too.
        /// </summary>
        /// <param name="reason">The disconnect reason (discReason)</param>
        /// <param name="extended">The ExtendedDisconnectReason: 0 none, 1-2 by this program, 11-12 by the user</param>
        /// <param name="wasLoggedIn">Whether the session had reached the desktop</param>
        /// <returns>true to show Windows' description of the disconnect</returns>
        public static bool ShouldReport(int reason, int extended, bool wasLoggedIn)
        {
            bool normal = reason >= 0 && reason <= 3;
            if (!normal)
                return true;
            // reason 1 is a disconnect from this side (e.g. the tab's own Disconnect), never a failure
            if (reason == 1)
                return false;
            return !wasLoggedIn || (extended >= 3 && extended <= 10) || extended >= 256;
        }

        /// <summary>Disconnect quietly when the tab closes</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            this.closing = true;
            if (disposing)
            {
                this.resizeTimer.Dispose();
                this.reconnectTimer.Dispose();
                this.cover?.Dispose();
                try
                {
                    if (this.client != null && (int)this.client.Connected != 0)
                    {
                        this.client.Disconnect();
                    }
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    // the control was already released, as after the server ended the session: nothing to disconnect
                }
                catch (Exception ex)
                {
                    Log.Warn("Error disconnecting while closing the tab", ex);
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// The remote desktop's scale for a display's DPI, as Remote Desktop takes them: the desktop scale in percent
        /// (100 to 500), and the device scale, which is only ever 100, 140 or 180
        /// </summary>
        /// <param name="dpi">The display's DPI (96 is 100 %)</param>
        /// <param name="desktopScale">The desktop scale factor, e.g. 150</param>
        /// <param name="deviceScale">The device scale factor: 100 below 125 %, 140 below 175 %, else 180</param>
        public static void ScaleFactors(int dpi, out uint desktopScale, out uint deviceScale)
        {
            int percent = (int)Math.Round(Math.Max(96, dpi) * 100.0 / 96);
            desktopScale = (uint)Math.Max(100, Math.Min(500, percent));
            deviceScale = desktopScale >= 175 ? 180u : desktopScale >= 125 ? 140u : 100u;
        }

        /// <summary>
        /// Give the connection the display's scale (<see cref="ScaleFactors"/>) through the control's extended settings,
        /// before connecting. A control too old for them connects at 100 %, as before.
        /// </summary>
        private void ApplyScaleFactors()
        {
            uint desktopScale, deviceScale;
            ScaleFactors(this.DeviceDpi, out desktopScale, out deviceScale);
            try
            {
                IMsRdpExtendedSettings extended = (IMsRdpExtendedSettings)this.GetOcx();
                object value = desktopScale;
                extended.put_Property("DesktopScaleFactor", ref value);
                value = deviceScale;
                extended.put_Property("DeviceScaleFactor", ref value);
                Log.InfoFormat("Remote desktop scale for {0}: {1} % (device {2} %)", this.session.Host, desktopScale, deviceScale);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not give the remote desktop the display's scale; it connects at 100 %", ex);
            }
        }

        /// <summary>
        /// The control's IMsRdpExtendedSettings interface, for settings that have no property of their own (the scale
        /// factors). IUnknown-only, so declared by hand; its two methods in vtable order.
        /// </summary>
        [ComImport, Guid("302D8188-0052-4807-806A-362B628F9AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsRdpExtendedSettings
        {
            /// <summary>Set a setting</summary>
            /// <param name="name">Its name, e.g. "DesktopScaleFactor"</param>
            /// <param name="value">Its value</param>
            void put_Property([MarshalAs(UnmanagedType.BStr)] string name, [In] ref object value);

            /// <summary>Read a setting</summary>
            /// <param name="name">Its name</param>
            /// <param name="value">Its value</param>
            void get_Property([MarshalAs(UnmanagedType.BStr)] string name, out object value);
        }

        /// <summary>Limit a size to what the control accepts</summary>
        /// <param name="size">The wanted desktop size</param>
        /// <returns>The size, with each side between 200 and 8192 pixels</returns>
        private static Size ClampDesktopSize(Size size)
        {
            return new Size(
                Math.Max(MinDesktopSize, Math.Min(MaxDesktopSize, size.Width)),
                Math.Max(MinDesktopSize, Math.Min(MaxDesktopSize, size.Height)));
        }

        /// <summary>
        /// The control's event interface (IMsTscAxEvents), declared by hand with only the events used. Other
        /// events reach IDispatch::Invoke with an unknown dispatch ID and are ignored.
        /// </summary>
        [ComImport, ComVisible(true), Guid("336d5562-efa8-482e-8cb3-c5c0fc7a7db6"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        public interface IMsTscAxEvents
        {
            /// <summary>The connection is up (Windows' login screen may follow)</summary>
            [DispId(2)] void OnConnected();

            /// <summary>Login completed</summary>
            [DispId(3)] void OnLoginComplete();

            /// <summary>The session ended</summary>
            /// <param name="discReason">The disconnect reason code</param>
            [DispId(4)] void OnDisconnected(int discReason);
        }

        /// <summary>
        /// The control's IMsRdpClientNonScriptable interface, which has SendKeys. It's IUnknown-only, so it can't be
        /// late bound; every method up to SendKeys is declared, in vtable order, so the call lands in the right slot.
        /// </summary>
        [ComImport, Guid("2f079c4c-87b2-4afd-97ab-20cdb43038ae"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMsRdpClientNonScriptable
        {
            // IMsTscNonScriptable
            void SetClearTextPassword([MarshalAs(UnmanagedType.BStr)] string password);
            void SetPortablePassword([MarshalAs(UnmanagedType.BStr)] string password);
            [return: MarshalAs(UnmanagedType.BStr)] string GetPortablePassword();
            void SetPortableSalt([MarshalAs(UnmanagedType.BStr)] string salt);
            [return: MarshalAs(UnmanagedType.BStr)] string GetPortableSalt();
            void SetBinaryPassword([MarshalAs(UnmanagedType.BStr)] string password);
            [return: MarshalAs(UnmanagedType.BStr)] string GetBinaryPassword();
            void SetBinarySalt([MarshalAs(UnmanagedType.BStr)] string salt);
            [return: MarshalAs(UnmanagedType.BStr)] string GetBinarySalt();
            void ResetPassword();

            // IMsRdpClientNonScriptable
            void NotifyRedirectDeviceChange(UIntPtr wParam, IntPtr lParam);

            /// <summary>Send key presses as scan codes</summary>
            /// <param name="numKeys">Number of entries in both arrays</param>
            /// <param name="pbArrayKeyUp">For each entry, VARIANT_BOOL: -1 for key up, 0 for key down</param>
            /// <param name="plKeyData">For each entry, the scan code</param>
            void SendKeys(int numKeys, [MarshalAs(UnmanagedType.LPArray)] short[] pbArrayKeyUp, [MarshalAs(UnmanagedType.LPArray)] int[] plKeyData);
        }

        /// <summary>Receives the control's events and passes them to the <see cref="RdpControl"/></summary>
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public class EventSink : IMsTscAxEvents
        {
            private readonly RdpControl owner;

            /// <summary>Create a sink that forwards to <paramref name="owner"/></summary>
            /// <param name="owner">The control receiving the events</param>
            public EventSink(RdpControl owner)
            {
                this.owner = owner;
            }

            /// <summary>Forward the connection being up</summary>
            public void OnConnected()
            {
                this.owner.OnConnected();
            }

            /// <summary>Forward login completion</summary>
            public void OnLoginComplete()
            {
                this.owner.OnLoginComplete();
            }

            /// <summary>Forward the end of the session</summary>
            /// <param name="discReason">The disconnect reason code</param>
            public void OnDisconnected(int discReason)
            {
                this.owner.OnDisconnected(discReason);
            }
        }
    }
}
