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
    public class RdpControl : AxHost, IEmbeddedApplication
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
            Size size = ClampDesktopSize(this.ClientSize);

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
            this.client.ColorDepth = 32;

            dynamic settings = this.client.AdvancedSettings9;
            settings.SmartSizing = true;
            settings.EnableCredSspSupport = true;
            // warn, but allow connecting, when the server's identity can't be verified, like mstsc does
            settings.AuthenticationLevel = 2u;
            if (!string.IsNullOrEmpty(this.session.Password))
            {
                settings.ClearTextPassword = this.session.Password;
            }

            Log.InfoFormat("Connecting to {0}:{1} as '{2}' with a {3}x{4} desktop", this.session.Host, this.session.Port, this.session.Username, size.Width, size.Height);
            this.client.Connect();
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
            if (this.loggedIn && !this.dynamicResizeUnsupported)
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
            if (!this.loggedIn || this.dynamicResizeUnsupported)
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
