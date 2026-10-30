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

        /// <summary>Create the control for a session; nothing connects until <see cref="Start"/></summary>
        /// <param name="session">The session, with {key} placeholders already replaced</param>
        public RdpControl(SessionData session) : base(ClsidMsRdpClient9NotSafeForScripting)
        {
            this.session = session;
            this.options = RdpOptions.EffectiveFor(session);
            this.resizeTimer = new Timer { Interval = ResizeDelayMs };
            this.resizeTimer.Tick += this.resizeTimer_Tick;
        }

        /// <summary>Raised when the session ends: disconnected, logged off, or failed to connect</summary>
        public event EventHandler Ended;

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

            Log.InfoFormat("Connecting to {0}:{1} as '{2}' with a {3}x{4} desktop{5}", this.session.Host, this.session.Port, this.session.Username, size.Width, size.Height,
                string.IsNullOrWhiteSpace(this.options.GatewayHost) ? "" : " through gateway " + this.options.GatewayHost);
            this.client.Connect();
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

        /// <summary>Convert a command to scan codes and pass them to the control in one SendKeys call</summary>
        /// <param name="command">The text and keys to send</param>
        private void SendKeystrokes(CommandData command)
        {
            if (!this.loggedIn || this.IsDisposed)
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
            if (command.KeyData != null)
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
                // desktop size, physical size, orientation (landscape), desktop and device scale (100%)
                this.client.UpdateSessionDisplaySettings((uint)size.Width, (uint)size.Height, (uint)size.Width, (uint)size.Height, 0u, 100u, 100u);
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
            Log.InfoFormat("Logged in to {0}", this.session.Host);
            // the tab may have been resized while connecting
            if (ClampDesktopSize(this.ClientSize) != new Size((int)this.client.DesktopWidth, (int)this.client.DesktopHeight))
            {
                this.resizeTimer.Start();
            }
        }

        /// <summary>
        /// Called when the session ends. Disconnecting or logging off ends it quietly; anything else (wrong host,
        /// failed login, network error) is reported with Windows' description of the error. Either way the tab closes.
        /// </summary>
        /// <param name="reason">The disconnect reason code</param>
        private void OnDisconnected(int reason)
        {
            this.loggedIn = false;
            if (this.closing)
                return;

            // 1: disconnected locally, 2: logged off or disconnected from the remote, 3: disconnected by the server
            bool normal = reason >= 0 && reason <= 3;
            Log.InfoFormat("Disconnected from {0}, reason {1}", this.session.Host, reason);
            if (!normal)
            {
                string description = "error " + reason;
                try
                {
                    description = this.client.GetErrorDescription((uint)reason, (uint)this.client.ExtendedDisconnectReason);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not get the disconnect description", ex);
                }
                Messenger.MessageBox($"Remote desktop session \"{this.session.SessionName}\" ended:\n\n{description}",
                    "Remote Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            this.Ended?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Disconnect quietly when the tab closes</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            this.closing = true;
            if (disposing)
            {
                this.resizeTimer.Dispose();
                try
                {
                    if (this.client != null && (int)this.client.Connected != 0)
                    {
                        this.client.Disconnect();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Error disconnecting while closing the tab", ex);
                }
            }
            base.Dispose(disposing);
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
