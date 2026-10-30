using System;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Edits one RDP session's overrides of Tools -> Options -> RDP (SessionData.RdpOptions, see <see cref="RdpOptions"/>).
    /// Every option can be left at Default: dropdowns start with "Default (...)" showing the global value, and the
    /// checkboxes are three-state, with the filled state meaning Default. Opened from Edit Session's "RDP options..." button.
    /// </summary>
    public partial class dlgRdpSessionOptions : Form
    {
        /// <summary>Colour depths offered, in dropdown order after Default</summary>
        private static readonly int[] ColorDepths = { 15, 16, 24, 32 };

        /// <summary>Remote audio choices, by stored value (0-2)</summary>
        private static readonly string[] AudioNames = { "Play on this computer", "Play on the remote computer", "Do not play" };

        /// <summary>Windows key combination choices, by stored value (0-2)</summary>
        private static readonly string[] KeyboardNames = { "On this computer", "On the remote computer", "Only when full screen" };

        /// <summary>Server authentication choices, by stored value (0-2)</summary>
        private static readonly string[] AuthNames = { "Connect without warning", "Do not connect", "Warn me" };

        /// <summary>The global options, shown as the Default choices</summary>
        private readonly RdpOptions defaults = RdpOptions.Defaults();

        /// <summary>Create the dialog showing a session's overrides</summary>
        /// <param name="overrides">The session's SessionData.RdpOptions text; empty for none</param>
        public dlgRdpSessionOptions(string overrides)
        {
            InitializeComponent();
            this.FillChoices();
            this.ShowOverrides(RdpOptions.Parse(overrides));

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>The overrides chosen, as SessionData.RdpOptions text; empty when everything is Default. Set when OK is clicked.</summary>
        public string Overrides { get; private set; }

        /// <summary>Fill the dropdowns, each starting with "Default (global value)", and describe the checkbox defaults in tooltips</summary>
        private void FillChoices()
        {
            string defaultSize = this.defaults.DisplayMode == "Fixed"
                ? string.Format("Fixed size {0}x{1}", this.defaults.FixedWidth, this.defaults.FixedHeight)
                : "Fit the tab";
            this.comboDisplayMode.Items.AddRange(new object[] { "Default (" + defaultSize + ")", "Fit the tab", "Fixed size" });
            this.comboColorDepth.Items.Add("Default (" + this.defaults.ColorDepth + "-bit)");
            this.comboColorDepth.Items.AddRange(ColorDepths.Select(d => (object)(d + "-bit")).ToArray());
            FillWithDefault(this.comboAudio, AudioNames, this.defaults.AudioMode.Value);
            FillWithDefault(this.comboKeyboard, KeyboardNames, this.defaults.KeyboardHookMode.Value);
            FillWithDefault(this.comboAuth, AuthNames, this.defaults.AuthenticationLevel.Value);

            // owned by the form's components, so it's disposed with the dialog
            if (this.components == null)
            {
                this.components = new System.ComponentModel.Container();
            }
            ToolTip tips = new ToolTip(this.components);
            Action<CheckBox, bool?> describe = (box, value) =>
                tips.SetToolTip(box, "Filled: Default (" + (value == true ? "on" : "off") + "). Ticked: on. Empty: off.");
            describe(this.checkSmartSizing, this.defaults.SmartSizing);
            describe(this.checkClipboard, this.defaults.RedirectClipboard);
            describe(this.checkDrives, this.defaults.RedirectDrives);
            describe(this.checkPrinters, this.defaults.RedirectPrinters);
            describe(this.checkSmartCards, this.defaults.RedirectSmartCards);
            describe(this.checkMicrophone, this.defaults.RedirectMicrophone);
            describe(this.checkConsole, this.defaults.ConnectToConsole);
            tips.SetToolTip(this.textBoxGateway, string.IsNullOrEmpty(this.defaults.GatewayHost)
                ? "Empty: Default (no gateway)"
                : "Empty: Default (" + this.defaults.GatewayHost + ")");
        }

        /// <summary>Fill a dropdown with "Default (name of the global value)" followed by the choices</summary>
        /// <param name="combo">The dropdown</param>
        /// <param name="names">The choices, by stored value</param>
        /// <param name="defaultValue">The global value</param>
        private static void FillWithDefault(ComboBox combo, string[] names, int defaultValue)
        {
            combo.Items.Add("Default (" + names[Math.Max(0, Math.Min(names.Length - 1, defaultValue))] + ")");
            combo.Items.AddRange(names.Cast<object>().ToArray());
        }

        /// <summary>Show a session's overrides; options it doesn't override show Default</summary>
        /// <param name="overrides">The parsed overrides</param>
        private void ShowOverrides(RdpOptions overrides)
        {
            this.comboDisplayMode.SelectedIndex = overrides.DisplayMode == null ? 0 : overrides.DisplayMode == "Fixed" ? 2 : 1;
            this.numericWidth.Value = Clamp(this.numericWidth, overrides.FixedWidth ?? this.defaults.FixedWidth.Value);
            this.numericHeight.Value = Clamp(this.numericHeight, overrides.FixedHeight ?? this.defaults.FixedHeight.Value);
            int depth = overrides.ColorDepth.HasValue ? Array.IndexOf(ColorDepths, overrides.ColorDepth.Value) : -1;
            this.comboColorDepth.SelectedIndex = depth + 1;
            ShowCheck(this.checkSmartSizing, overrides.SmartSizing);
            ShowCheck(this.checkClipboard, overrides.RedirectClipboard);
            ShowCheck(this.checkDrives, overrides.RedirectDrives);
            ShowCheck(this.checkPrinters, overrides.RedirectPrinters);
            ShowCheck(this.checkSmartCards, overrides.RedirectSmartCards);
            ShowCheck(this.checkMicrophone, overrides.RedirectMicrophone);
            ShowCheck(this.checkConsole, overrides.ConnectToConsole);
            this.comboAudio.SelectedIndex = OverrideIndex(overrides.AudioMode, AudioNames.Length);
            this.comboKeyboard.SelectedIndex = OverrideIndex(overrides.KeyboardHookMode, KeyboardNames.Length);
            this.comboAuth.SelectedIndex = OverrideIndex(overrides.AuthenticationLevel, AuthNames.Length);
            this.checkNoGateway.Checked = overrides.GatewayHost == string.Empty;
            this.textBoxGateway.Text = string.IsNullOrEmpty(overrides.GatewayHost) ? string.Empty : overrides.GatewayHost;
            this.comboDisplayMode_SelectedIndexChanged(this, EventArgs.Empty);
        }

        /// <summary>Read the dialog back into overrides and close with OK</summary>
        /// <param name="e">Not used</param>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (this.DialogResult != DialogResult.OK)
                return;

            int display = this.comboDisplayMode.SelectedIndex;
            RdpOptions overrides = new RdpOptions
            {
                DisplayMode = display == 1 ? "FitTab" : display == 2 ? "Fixed" : null,
                FixedWidth = display == 2 ? (int)this.numericWidth.Value : (int?)null,
                FixedHeight = display == 2 ? (int)this.numericHeight.Value : (int?)null,
                ColorDepth = this.comboColorDepth.SelectedIndex > 0 ? ColorDepths[this.comboColorDepth.SelectedIndex - 1] : (int?)null,
                SmartSizing = ReadCheck(this.checkSmartSizing),
                RedirectClipboard = ReadCheck(this.checkClipboard),
                RedirectDrives = ReadCheck(this.checkDrives),
                RedirectPrinters = ReadCheck(this.checkPrinters),
                RedirectSmartCards = ReadCheck(this.checkSmartCards),
                RedirectMicrophone = ReadCheck(this.checkMicrophone),
                ConnectToConsole = ReadCheck(this.checkConsole),
                AudioMode = this.comboAudio.SelectedIndex > 0 ? this.comboAudio.SelectedIndex - 1 : (int?)null,
                KeyboardHookMode = this.comboKeyboard.SelectedIndex > 0 ? this.comboKeyboard.SelectedIndex - 1 : (int?)null,
                AuthenticationLevel = this.comboAuth.SelectedIndex > 0 ? this.comboAuth.SelectedIndex - 1 : (int?)null,
                GatewayHost = this.checkNoGateway.Checked ? string.Empty
                    : string.IsNullOrWhiteSpace(this.textBoxGateway.Text) ? null : this.textBoxGateway.Text.Trim()
            };
            this.Overrides = overrides.Format();
        }

        /// <summary>Enable the width and height only for a fixed size chosen for this session</summary>
        /// <param name="sender">The desktop size dropdown</param>
        /// <param name="e">Not used</param>
        private void comboDisplayMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isFixed = this.comboDisplayMode.SelectedIndex == 2;
            this.numericWidth.Enabled = isFixed;
            this.numericHeight.Enabled = isFixed;
        }

        /// <summary>"Don't use a gateway" makes the gateway box irrelevant</summary>
        /// <param name="sender">The checkbox</param>
        /// <param name="e">Not used</param>
        private void checkNoGateway_CheckedChanged(object sender, EventArgs e)
        {
            this.textBoxGateway.Enabled = !this.checkNoGateway.Checked;
        }

        /// <summary>Show an override on a three-state checkbox: null is the filled (Default) state</summary>
        private static void ShowCheck(CheckBox box, bool? value)
        {
            box.CheckState = value == null ? CheckState.Indeterminate : value.Value ? CheckState.Checked : CheckState.Unchecked;
        }

        /// <summary>Read a three-state checkbox: the filled (Default) state is null</summary>
        private static bool? ReadCheck(CheckBox box)
        {
            return box.CheckState == CheckState.Indeterminate ? (bool?)null : box.CheckState == CheckState.Checked;
        }

        /// <summary>The dropdown index for an override: 0 (Default) when not overridden, otherwise value + 1</summary>
        private static int OverrideIndex(int? value, int count)
        {
            return value.HasValue && value.Value >= 0 && value.Value < count ? value.Value + 1 : 0;
        }

        /// <summary>Keep a number within a number box's range</summary>
        private static decimal Clamp(NumericUpDown box, int value)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }
    }
}
