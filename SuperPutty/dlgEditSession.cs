/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
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

using DarkModeForms;
using log4net;
using SuperPutty.Data;
using SuperPutty.Gui;
using SuperPutty.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace SuperPutty
{
    public partial class dlgEditSession : Form
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(dlgEditSession));

        public delegate bool SessionNameValidationHandler(string name, out string error);

        private SessionData Session;

        /// <summary>This session's RDP option overrides (SessionData.RdpOptions), as edited with "RDP options..."; saved on Save</summary>
        private string rdpOptions;
        private String OldHostname;
        private bool isInitialized = false;

        private static readonly Color placeholderMissingColor = Color.IndianRed;
        private Dictionary<string, string> placeholderValues;
        private Color placeholderTextColor = Color.Empty;
        private Color placeholderPreviewColor;
        private System.Collections.Hashtable protoTypesMap;

        public dlgEditSession(SessionData session, ImageList iconList)
        {
            Session = session;
            InitializeComponent();
            placeholderValues = SessionVariables.Load(true);

            // grey examples in the empty fields, gone as soon as something is typed
            HintText.Set(this.textBoxSessionName, "e.g. core-sw1");
            HintText.Set(this.textBoxHostname, "e.g. 192.168.0.5");
            // left empty, the session uses its folder's default username (Folder settings), so say which
            string inherited = string.IsNullOrEmpty(session.SessionId) ? null : FolderSettings.Describe(session, "Username");
            HintText.Set(this.textBoxUsername, inherited ?? "e.g. admin, or {user}");
            string inheritedJump = string.IsNullOrEmpty(session.SessionId) ? null : FolderSettings.Describe(session, "JumpHost");
            HintText.Set(this.textBoxJumpHost, inheritedJump ?? "none; or a session, e.g. Bastions/bastion1, or admin@10.0.0.1:22");
            this.toolTip.SetToolTip(this.textBoxJumpHost, "Connect through this SSH server (SSH, Telnet, Rlogin, Raw): a saved session's path, or user@host:port. "
                + "Uses plink beside PuTTY; the jump host must accept a key (Pageant, or a key in its PuTTY profile).");
            // left on the first choice, the session takes its folder's rules (Folder settings), else Options -> Terminal's
            string inheritedRules = string.IsNullOrEmpty(session.SessionId) ? null : FolderSettings.Describe(session, "HighlightRules");
            OutputHighlighting.FillRuleChoices(this.comboBoxHighlightRules, inheritedRules != null ? "Folder's: " + inheritedRules : "Default (Tools > Options)", session.HighlightRules);
            this.toolTip.SetToolTip(this.comboBoxHighlightRules, "The built-in terminal's highlighting rules for this session (Tools > Options > Terminal turns the terminal on)");
            this.color = session.Color;
            this.ShowColor();

            // draw the profile list ourselves so a profile that isn't saved in PuTTY/KiTTY shows in red; the dark
            // theme draws a drop-down list's text in its own colour, ignoring ForeColor
            comboBoxPuttyProfile.DrawMode = DrawMode.OwnerDrawFixed;
            comboBoxPuttyProfile.ItemHeight = comboBoxPuttyProfile.Font.Height + 4;
            comboBoxPuttyProfile.DrawItem += comboBoxPuttyProfile_DrawItem;

            // get putty saved settings from the registry to populate
            // the dropdown
            PopulatePuttySettings();
            PopulateProtoList();

            if (!String.IsNullOrEmpty(Session.SessionName))
            {
                this.Text = "Edit session: " + session.SessionName;
                this.textBoxSessionName.Text = Session.SessionName;
                this.textBoxHostname.Text = Session.Host;
                this.textBoxPort.Text = Session.Port.ToString();
                this.textBoxExtraArgs.Text = Session.ExtraArgs;
                this.textBoxJumpHost.Text = Session.JumpHost;
                this.textBoxUsername.Text = Session.Username;
                this.textBoxSPSLScriptFile.Text = Session.SPSLFileName;
                this.textBoxRemotePathSesion.Text = Session.RemotePath;
                this.textBoxLocalPathSesion.Text = Session.LocalPath;
                this.textBoxNote.Text = Session.Note;

                foreach (System.Collections.DictionaryEntry protoEntry in this.protoTypesMap)
                {
                    if ((int)protoEntry.Value == (int)Session.Proto)
                        comboBoxProto.SelectedItem = (string)protoEntry.Key;
                }

                comboBoxPuttyProfile.DropDownStyle = ComboBoxStyle.DropDownList;
                // a profile that isn't saved in PuTTY/KiTTY (yet) is still listed and selected, so Save keeps it
                // instead of quietly replacing it with Default Settings
                if (!string.IsNullOrEmpty(session.PuttySession) && !this.comboBoxPuttyProfile.Items.Contains(session.PuttySession))
                {
                    this.comboBoxPuttyProfile.Items.Add(session.PuttySession);
                }
                foreach(String settings in this.comboBoxPuttyProfile.Items){
                    if (settings == session.PuttySession)
                    {
                        this.comboBoxPuttyProfile.SelectedItem = settings;
                        break;
                    }
                }

                this.buttonSave.Enabled = true;
            }
            else
            {
                this.Text = "Create new session";
                comboBoxProto.SelectedItem = "SSH";
                this.buttonSave.Enabled = false;
            }


            // Setup icon chooser
            this.buttonImageSelect.ImageList = iconList;
            // coloured icons ("switch#1E63B4") are drawn into the list on first use
            this.buttonImageSelect.ImageKey = !IconLibrary.Ensure(iconList, Session.ImageKey)
                ? SessionTreeview.ImageKeySession
                : Session.ImageKey;
            this.toolTip.SetToolTip(this.buttonImageSelect, buttonImageSelect.ImageKey);

            this.isInitialized = true;

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>
        /// Show what the {key} placeholders in the host, username and extra arguments resolve to,
        /// or highlight in red the ones that have no value yet
        /// </summary>
        private void UpdatePlaceholderPreview()
        {
            if (placeholderTextColor.IsEmpty)
                return;

            List<string> missing = new List<string>();
            bool hasPlaceholders = false;
            foreach (TextBox textBox in new[] { textBoxHostname, textBoxUsername, textBoxExtraArgs })
            {
                List<string> fieldMissing = SessionVariables.FindMissing(textBox.Text, placeholderValues);
                textBox.ForeColor = fieldMissing.Count > 0 ? placeholderMissingColor : placeholderTextColor;
                missing.AddRange(fieldMissing);
                hasPlaceholders |= SessionVariables.Substitute(textBox.Text, placeholderValues) != textBox.Text;
            }
            missing = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            string text = string.Empty;
            if (missing.Count > 0)
            {
                text = $"Undefined: {string.Join(", ", missing)}. Click here to define them.";
            }
            else if (hasPlaceholders)
            {
                string host = SessionVariables.Substitute(textBoxHostname.Text, placeholderValues);
                string user = SessionVariables.Substitute(textBoxUsername.Text, placeholderValues);
                text = string.IsNullOrEmpty(user) ? $"Connects to {host}" : $"Connects as {user} to {host}";
            }

            labelPlaceholderPreview.Text = text;
            labelPlaceholderPreview.ForeColor = missing.Count > 0 ? placeholderMissingColor : placeholderPreviewColor;
            labelPlaceholderPreview.Cursor = missing.Count > 0 ? Cursors.Hand : Cursors.Default;
            toolTip.SetToolTip(labelPlaceholderPreview, text);
        }

        private void placeholderField_TextChanged(object sender, EventArgs e)
        {
            UpdatePlaceholderPreview();
            if (sender == this.textBoxHostname)
            {
                // a host typed after the error was shown clears it now, not only when the box is left
                if (this.errorProvider.GetError(this.textBoxHostname) != String.Empty && this.textBoxHostname.Text.Trim() != String.Empty)
                {
                    this.SetError(this.textBoxHostname, String.Empty);
                    this.SetError(this.comboBoxPuttyProfile, String.Empty);
                }
                this.EnableDisableSaveButton();
            }
        }

        /// <summary>
        /// Check the session name as it's typed, not only when the box is left: otherwise an "Empty name" error from
        /// tabbing past the box first kept Save disabled until the box was left again
        /// </summary>
        /// <param name="sender">The session name box</param>
        /// <param name="e">Not used</param>
        private void textBoxSessionName_TextChanged(object sender, EventArgs e)
        {
            string error = null;
            if (this.SessionNameValidator != null && !this.SessionNameValidator(this.textBoxSessionName.Text, out error))
            {
                this.SetError(this.textBoxSessionName, error ?? "Invalid Session Name");
            }
            else
            {
                this.SetError(this.textBoxSessionName, String.Empty);
            }
        }

        private void labelPlaceholderPreview_Click(object sender, EventArgs e)
        {
            using (EnvironmentVariablesForm envForm = new EnvironmentVariablesForm())
            {
                envForm.ShowDialog(this);
            }
            placeholderValues = SessionVariables.Load(true);
            UpdatePlaceholderPreview();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // DarkModeCS themes the controls during Load, so capture the themed colors only now,
            // letting fields switch back from red after a missing placeholder is defined
            placeholderTextColor = textBoxUsername.ForeColor;
            placeholderPreviewColor = label5.ForeColor;
            UpdatePlaceholderPreview();
            ShowProfileMissing();

            this.rdpOptions = Session.RdpOptions;
            UpdateRdpOptionsButton();
            // after everything that enables controls, so a locked one stays locked
            ApplyLockedFields();
            AddOverridesButton();

            this.BeginInvoke(new MethodInvoker(delegate { this.textBoxSessionName.Focus(); }));
        }

        /// <summary>The session's own environment colour as chosen here, "#RRGGBB", or empty for none</summary>
        private string color;

        /// <summary>
        /// Show the chosen colour on the Colour button: a swatch, or an empty box with a slash for none. The tooltip says
        /// what the session shows without one of its own (its folder's or a rule's colour).
        /// </summary>
        private void ShowColor()
        {
            int size = this.LogicalToDeviceUnits(22);
            Bitmap swatch = new Bitmap(size, size);
            Color? own = EnvironmentColors.Parse(this.color);
            using (Graphics g = Graphics.FromImage(swatch))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (own != null)
                {
                    using (SolidBrush brush = new SolidBrush(own.Value))
                    {
                        g.FillRectangle(brush, 0, 0, size - 1, size - 1);
                    }
                }
                else
                {
                    g.DrawLine(Pens.Gray, 1, size - 2, size - 2, 1);
                }
                g.DrawRectangle(Pens.Gray, 0, 0, size - 1, size - 1);
            }
            this.buttonColor.Image?.Dispose();
            this.buttonColor.Image = swatch;

            string tip;
            if (own != null)
            {
                tip = "This session's colour: " + this.color + " (shown on its tab and around its terminal)";
            }
            else
            {
                SessionData without = (SessionData)this.Session.Clone();
                without.Color = string.Empty;
                Color? inherited = string.IsNullOrEmpty(this.Session.SessionId) ? null : EnvironmentColors.For(without);
                tip = inherited == null ? "No colour. Choose one, e.g. red for production"
                    : "No colour of its own: it shows " + EnvironmentColors.Format(inherited.Value) + " from its folder or a name rule";
            }
            this.toolTip.SetToolTip(this.buttonColor, tip);
        }

        /// <summary>Choose the session's colour: the presets, Other colour..., or None</summary>
        /// <param name="sender">The Colour button</param>
        /// <param name="e">Not used</param>
        private void buttonColor_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem holder = new ToolStripMenuItem();
            EnvironmentColors.FillMenu(holder, this.color, "None (use the folder's or a rule's)", chosen =>
            {
                this.color = chosen;
                this.ShowColor();
            });
            // shown as its own menu: the holder's drop-down belongs to an item on no menu, and WinForms then placed it
            // at the top-left of the screen instead of under the button
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.AddRange(holder.DropDownItems.Cast<ToolStripItem>().ToArray());
            menu.Closed += (s, args) => this.BeginInvoke((MethodInvoker)menu.Dispose);
            MenuCheckMarks.Apply(menu);
            menu.Show(this.buttonColor, new Point(0, this.buttonColor.Height));
        }

        /// <summary>
        /// The ID of the saved session being edited, for its local overrides (Data.SessionOverrides); null for a new
        /// session or a copy, which have none. Set by SessionTreeview before showing the dialog.
        /// </summary>
        public string OverridesSessionId { get; set; }

        /// <summary>The "Local overrides" button, beside Save; null when there's no saved session to override</summary>
        private Button buttonOverrides;

        /// <summary>Each overridable field's own tooltip, put back when its override is removed</summary>
        private readonly Dictionary<Control, string> fieldToolTips = new Dictionary<Control, string>();

        /// <summary>
        /// Add the "Local overrides" button to the left of Save, for a saved session: it opens the user's overrides for
        /// it (dlgSessionOverrides) and saves them at once, whether or not the session is saved. Added here rather than
        /// in the designer, after the form is scaled, so it lines up with Save at any DPI.
        /// </summary>
        private void AddOverridesButton()
        {
            if (string.IsNullOrEmpty(this.OverridesSessionId))
                return;
            int left = this.groupBoxFileTransferOptions.Left;
            this.buttonOverrides = new Button
            {
                Location = new Point(left, this.buttonSave.Top),
                Size = new Size(this.buttonSave.Left - left - this.LogicalToDeviceUnits(12), this.buttonSave.Height),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                FlatStyle = this.buttonSave.FlatStyle,
                BackColor = this.buttonSave.BackColor,
                UseVisualStyleBackColor = this.buttonSave.UseVisualStyleBackColor
            };
            this.buttonOverrides.FlatAppearance.BorderColor = this.buttonCancel.FlatAppearance.BorderColor;
            this.buttonOverrides.Click += (s, e) => this.EditOverrides();
            this.Controls.Add(this.buttonOverrides);
            foreach (Control field in this.OverridableFields().Values)
            {
                this.fieldToolTips[field] = this.toolTip.GetToolTip(field);
            }
            this.ShowOverrides();
        }

        /// <summary>The fields a local override can replace, by the name the overrides use</summary>
        /// <returns>"Host", "Port", "Username", "PuTTY profile", "Extra args" and "Jump host", with their editors</returns>
        private Dictionary<string, Control> OverridableFields()
        {
            return new Dictionary<string, Control>
            {
                { "Host", this.textBoxHostname },
                { "Port", this.textBoxPort },
                { "Username", this.textBoxUsername },
                { "PuTTY profile", this.comboBoxPuttyProfile },
                { "Extra args", this.textBoxExtraArgs },
                { "Jump host", this.textBoxJumpHost }
            };
        }

        /// <summary>
        /// Show the session's local overrides: the button names the fields, in an accent colour, and each overridden
        /// field's tooltip says which value is used instead
        /// </summary>
        private void ShowOverrides()
        {
            SessionOverrides.Override local = SessionOverrides.Get(this.OverridesSessionId);
            List<string> fields = local == null ? new List<string>() : local.Fields();
            this.buttonOverrides.Text = fields.Count == 0 ? "Local overrides..." : "Local overrides: " + string.Join(", ", fields) + "...";
            bool dark = SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;
            this.buttonOverrides.ForeColor = fields.Count == 0 ? this.buttonSave.ForeColor
                : dark ? Color.FromArgb(0xF0, 0xA8, 0x3C) : Color.FromArgb(0xB0, 0x5A, 0x00);
            this.toolTip.SetToolTip(this.buttonOverrides, fields.Count == 0
                ? "Your own values for this session, used when it opens without changing it; only you get them"
                : "You have your own values for " + string.Join(", ", fields) + ": they're used when this session opens, instead of the ones here");
            foreach (KeyValuePair<string, Control> field in this.OverridableFields())
            {
                string tip = this.fieldToolTips[field.Value];
                if (fields.Contains(field.Key))
                {
                    tip = "Overridden locally: your value " + OverrideValue(local, field.Key) + " is used when this session opens"
                        + (string.IsNullOrEmpty(tip) ? "" : Environment.NewLine + tip);
                }
                this.toolTip.SetToolTip(field.Value, tip);
            }
        }

        /// <summary>An override's value for a field, quoted, for the field's tooltip</summary>
        /// <param name="local">The overrides</param>
        /// <param name="field">The field's name, as in <see cref="OverridableFields"/></param>
        /// <returns>e.g. "admin", or "(none)" for an empty one</returns>
        private static string OverrideValue(SessionOverrides.Override local, string field)
        {
            string value = field == "Host" ? local.Host : field == "Port" ? local.Port.ToString()
                : field == "Username" ? local.Username : field == "PuTTY profile" ? local.PuttyProfile
                : field == "Jump host" ? local.JumpHost : null;
            // extra arguments can hold a -pw password: not shown
            if (field == "Extra args")
                return "(set in Local overrides)";
            return string.IsNullOrEmpty(value) ? "(none)" : "\"" + value + "\"";
        }

        /// <summary>Edit the session's local overrides, save them at once, and show them</summary>
        private void EditOverrides()
        {
            using (dlgSessionOverrides dialog = new dlgSessionOverrides(this.Session, SessionOverrides.Get(this.OverridesSessionId)))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SessionOverrides.Set(this.OverridesSessionId, dialog.Result);
                    this.ShowOverrides();
                }
            }
        }

        /// <summary>
        /// Database columns locked for this session (the row's locked_fields, comma-separated, e.g. "host,port"): their
        /// fields are shown but can't be changed. Set by SessionTreeview before showing the dialog for a database session.
        /// </summary>
        public string LockedFields { get; set; }

        /// <summary>
        /// Make the fields for <see cref="LockedFields"/> read-only (text boxes, so the value can still be selected and
        /// copied) or disabled (lists and buttons), with a tooltip saying why
        /// </summary>
        private void ApplyLockedFields()
        {
            if (string.IsNullOrEmpty(this.LockedFields))
                return;
            Dictionary<string, Control[]> fields = new Dictionary<string, Control[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "name", new Control[] { this.textBoxSessionName } },
                { "protocol", new Control[] { this.comboBoxProto } },
                { "host", new Control[] { this.textBoxHostname } },
                { "port", new Control[] { this.textBoxPort } },
                { "username", new Control[] { this.textBoxUsername } },
                { "putty_profile", new Control[] { this.comboBoxPuttyProfile } },
                { "extra_args", new Control[] { this.textBoxExtraArgs } },
                { "jump_host", new Control[] { this.textBoxJumpHost } },
                { "highlight_rules", new Control[] { this.comboBoxHighlightRules } },
                { "color", new Control[] { this.buttonColor } },
                { "icon", new Control[] { this.buttonImageSelect } },
                { "notes", new Control[] { this.textBoxNote } },
                { "spsl_script", new Control[] { this.textBoxSPSLScriptFile, this.buttonBrowse, this.buttonClearSPSLFile } },
                { "rdp_options", new Control[] { this.buttonRdpOptions } },
                { "local_path", new Control[] { this.textBoxLocalPathSesion, this.buttonBrowseLocalPath } },
                { "remote_path", new Control[] { this.textBoxRemotePathSesion } }
            };
            bool any = false;
            foreach (string column in this.LockedFields.Split(','))
            {
                Control[] controls;
                if (!fields.TryGetValue(column.Trim(), out controls))
                    continue;
                any = true;
                foreach (Control control in controls)
                {
                    TextBox text = control as TextBox;
                    if (text != null)
                    {
                        // read-only looks like editable in the dark theme, so show it flat and dimmed
                        text.ReadOnly = true;
                        text.BackColor = this.BackColor;
                        text.ForeColor = Color.FromArgb(0x9A, 0x9A, 0x9A);
                    }
                    else
                        control.Enabled = false;
                    this.toolTip.SetToolTip(control, "Locked in the database: SuperXPuTTY can't change this field");
                }
            }
            if (any)
            {
                this.Text += " (greyed fields are locked in the database)";
            }
        }

        /// <summary>Enable "RDP options..." for RDP sessions only, and mark it with a tick when this session overrides something</summary>
        private void UpdateRdpOptionsButton()
        {
            bool isRdp = this.comboBoxProto.SelectedItem != null && this.protoTypesMap.ContainsKey(this.comboBoxProto.SelectedItem)
                && (ConnectionProtocol)this.protoTypesMap[this.comboBoxProto.SelectedItem] == ConnectionProtocol.RDP;
            this.buttonRdpOptions.Enabled = isRdp;
            this.buttonRdpOptions.Text = string.IsNullOrEmpty(this.rdpOptions) ? "RDP options..." : "RDP options ✓";
        }

        /// <summary>Edit this session's overrides of Tools -> Options -> RDP; they're saved with the session on Save</summary>
        /// <param name="sender">The RDP options button</param>
        /// <param name="e">Not used</param>
        private void buttonRdpOptions_Click(object sender, EventArgs e)
        {
            using (dlgRdpSessionOptions dialog = new dlgRdpSessionOptions(this.rdpOptions))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.rdpOptions = dialog.Overrides;
                    UpdateRdpOptionsButton();
                }
            }
        }

        /// <summary>The profiles saved in PuTTY/KiTTY when the dialog opened, so drawing the list doesn't re-read them</summary>
        private List<string> savedProfiles = new List<string>();

        /// <summary>Whether a profile is saved in PuTTY/KiTTY, or needs no saving (empty, Default Settings)</summary>
        /// <param name="profile">The profile name</param>
        /// <returns>true unless it's missing</returns>
        private bool IsProfileSaved(string profile)
        {
            return string.IsNullOrEmpty(profile) || profile == PuttyDataHelper.SessionDefaultSettings || savedProfiles.Contains(profile);
        }

        private void PopulatePuttySettings()
        {
            savedProfiles = PuttyDataHelper.GetSessionNames();
            foreach (String sessionName in savedProfiles)
            {
                comboBoxPuttyProfile.Items.Add(sessionName);
            }
            comboBoxPuttyProfile.SelectedItem = PuttyDataHelper.SessionDefaultSettings;
        }

        private void PopulateProtoList()
        {
            this.protoTypesMap = new System.Collections.Hashtable();
            this.protoTypesMap["SSH"] = ConnectionProtocol.SSH;
            this.protoTypesMap["Telnet"] = ConnectionProtocol.Telnet;
            this.protoTypesMap["Rlogin"] = ConnectionProtocol.Rlogin;
            this.protoTypesMap["Raw"] = ConnectionProtocol.Raw;
            this.protoTypesMap["Serial"] = ConnectionProtocol.Serial;
            this.protoTypesMap["VNC"] = ConnectionProtocol.VNC;
            this.protoTypesMap["RDP"] = ConnectionProtocol.RDP;
            this.protoTypesMap["CygTerm"] = ConnectionProtocol.Cygterm;
            this.protoTypesMap["MinTTY"] = ConnectionProtocol.Mintty;
            this.protoTypesMap["WSL"] = ConnectionProtocol.WSL;
            this.protoTypesMap["Win CMD"] = ConnectionProtocol.WINCMD;
            this.protoTypesMap["PowerShell"] = ConnectionProtocol.PS;


            foreach (System.Collections.DictionaryEntry protoEntry in this.protoTypesMap)
                comboBoxProto.Items.Add(protoEntry.Key);
            comboBoxProto.SelectedItem = "SSH";
            comboBoxProto.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {

            int val = 0;
            if (!string.IsNullOrEmpty(CommandLineOptions.getcommand(textBoxExtraArgs.Text, "-pw")))
            {
                if (Messenger.MessageBox("SuperPutty saves the extra arguments Sessions.xml file in plain text.\n" +
                                         "Use of -pw password in 'Extra arguments' is very insecure.\n" +
                                         "For a secure connection use SSH authentication with Pageant.\n" +
                                         "Alternatively, use -pwfile to specify a password file or enable -pw in Tools->Options->Advanced.\n" +
                                         "\nSelect Yes, if you still want save the password.",
                                         "Are you sure that you want to save the password?",
                                         MessageBoxButtons.YesNo,
                                         MessageBoxIcon.Warning) == DialogResult.No)
                {
                    return;
                }
            }
            Session.SessionName  = textBoxSessionName.Text.Trim();
            Session.PuttySession = comboBoxPuttyProfile.Text.Trim();
            Session.Host         = textBoxHostname.Text.Trim();
            Session.ExtraArgs    = textBoxExtraArgs.Text.Trim();
            Session.JumpHost     = textBoxJumpHost.Text.Trim();
            Session.HighlightRules = OutputHighlighting.SelectedRuleChoice(comboBoxHighlightRules);
            Session.Color       = this.color ?? string.Empty;
            // open tabs of this session show the new colour now, not at the next change
            EnvironmentColors.RaiseChanged();
            if (!Int32.TryParse(this.textBoxPort.Text, out val))
                Session.Port     = 0;
            else
                Session.Port     = int.Parse(textBoxPort.Text.Trim());
            Session.Username     = textBoxUsername.Text.Trim();
            Session.SessionId    = SessionData.CombineSessionIds(SessionData.GetSessionParentId(Session.SessionId), Session.SessionName);
            Session.ImageKey     = buttonImageSelect.ImageKey;
            Session.SPSLFileName = textBoxSPSLScriptFile.Text.Trim();
            Session.RdpOptions   = this.rdpOptions;
            Session.RemotePath = textBoxRemotePathSesion.Text.Trim();
            Session.LocalPath = textBoxLocalPathSesion.Text.Trim();
            Session.Note = textBoxNote.Text.Trim();

            if (this.protoTypesMap.ContainsKey(comboBoxProto.SelectedItem))
                Session.Proto = (ConnectionProtocol)this.protoTypesMap[comboBoxProto.SelectedItem];
            
            DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// Special UI handling for cygterm or mintty sessions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void comboBoxProto_SelectedIndexChanged(object sender, EventArgs e)
        {
            string host = this.textBoxHostname.Text;
            if (!this.isInitialized)
                return;

            ConnectionProtocol proto = this.protoTypesMap.ContainsKey(comboBoxProto.SelectedItem) ? (ConnectionProtocol)this.protoTypesMap[comboBoxProto.SelectedItem] : ConnectionProtocol.SSH;
            if (proto == ConnectionProtocol.Cygterm || proto == ConnectionProtocol.Mintty || proto == ConnectionProtocol.WINCMD || proto == ConnectionProtocol.PS || proto == ConnectionProtocol.WSL)
            {
                this.textBoxPort.Enabled = false;
                this.textBoxUsername.Enabled = false;
                lblHostname.Text = "Host address:";
                if (String.IsNullOrEmpty(host) || !host.StartsWith(CygtermStartInfo.LocalHost))
                {
                    OldHostname = this.textBoxHostname.Text;
                    this.textBoxHostname.Text = CygtermStartInfo.LocalHost;
                }
            }
            else if (proto == ConnectionProtocol.Serial)
            {
                textBoxPort.Enabled = false;
                textBoxUsername.Enabled = false;
                lblHostname.Text = "Serial port:";
                textBoxExtraArgs.Text = "-sercfg 115200,8,n,1,N";
            }
            else
            {
                this.textBoxPort.Enabled = true;
                this.textBoxUsername.Enabled = true;
                lblHostname.Text = "Host address:";
                if (!string.IsNullOrEmpty(OldHostname))
                {
                    this.textBoxHostname.Text = OldHostname;
                    OldHostname = null;
                }
                if (proto != ConnectionProtocol.Raw)
                    this.textBoxPort.Text = dlgEditSession.GetDefaultPort(proto).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            UpdateRdpOptionsButton();

            // Default icons, only in place of another protocol's default: an icon chosen for the session (a copy's
            // included) stays when the connection type changes
            if (!IsProtocolIcon(buttonImageSelect.ImageKey))
                return;
            switch (proto)
            {
                case ConnectionProtocol.SSH:
                case ConnectionProtocol.Rlogin:
                    buttonImageSelect.ImageKey = "computer";
                    break;
                case ConnectionProtocol.Telnet:
                case ConnectionProtocol.Serial:
                case ConnectionProtocol.Raw:
                    buttonImageSelect.ImageKey = "application_osx_terminal";
                    break;
                case ConnectionProtocol.VNC:
                case ConnectionProtocol.RDP:
                    buttonImageSelect.ImageKey = "map";
                    break;
                case ConnectionProtocol.WINCMD:
                case ConnectionProtocol.PS:
                case ConnectionProtocol.Cygterm:
                case ConnectionProtocol.Mintty:
                    buttonImageSelect.ImageKey = "application_xp_terminal";
                    break;
                case ConnectionProtocol.WSL:
                    buttonImageSelect.ImageKey = "ubuntu";
                    break;
            }
        }

        /// <summary>
        /// Whether an icon is one of the defaults the connection type sets (or the plain session icon), which a change of
        /// connection type may replace; any other was chosen for the session and is kept
        /// </summary>
        /// <param name="imageKey">The icon's key</param>
        /// <returns>true for a default icon, or none</returns>
        public static bool IsProtocolIcon(string imageKey)
        {
            return string.IsNullOrEmpty(imageKey) || imageKey == SessionTreeview.ImageKeySession
                || new[] { "computer", "application_osx_terminal", "map", "application_xp_terminal", "ubuntu" }.Contains(imageKey);
        }

        public static int GetDefaultPort(ConnectionProtocol protocol)
        {
            int port = 22;
            switch (protocol)
            {
                case ConnectionProtocol.Raw:
                    break;
                case ConnectionProtocol.Rlogin:
                    port = 513;
                    break;
                case ConnectionProtocol.Serial:
                    break;
                case ConnectionProtocol.Telnet:
                    port = 23;
                    break;
                case ConnectionProtocol.VNC:
                    port = 5900;
                    break;
                case ConnectionProtocol.RDP:
                    port = 3389;
                    break;
                case ConnectionProtocol.WINCMD:
                case ConnectionProtocol.PS:
                case ConnectionProtocol.WSL:
                    port = 0;
                    break;
            }
            return port;
        }

        #region Icon
        /// <summary>
        /// Choose the session's icon, and for device templates and logos its colour, in the icon picker
        /// (dlgIconPicker), which replaced the plain grid popup
        /// </summary>
        /// <param name="sender">The icon button</param>
        /// <param name="e">Not used</param>
        private void buttonImageSelect_Click(object sender, EventArgs e)
        {
            using (dlgIconPicker picker = new dlgIconPicker(buttonImageSelect.ImageKey))
            {
                if (picker.ShowDialog(this) == DialogResult.OK && IconLibrary.Ensure(buttonImageSelect.ImageList, picker.SelectedKey))
                {
                    buttonImageSelect.ImageKey = picker.SelectedKey;
                    this.toolTip.SetToolTip(this.buttonImageSelect, buttonImageSelect.ImageKey);
                }
            }
        }


        #endregion

        #region Validation Logic

        public SessionNameValidationHandler SessionNameValidator { get; set; }

        private void textBoxSessionName_Validating(object sender, CancelEventArgs e)
        {
            if (this.SessionNameValidator != null)
            {
                string error;
                if (!this.SessionNameValidator(this.textBoxSessionName.Text, out error))
                {
                    e.Cancel = true;
                    this.SetError(this.textBoxSessionName, error ?? "Invalid Session Name");
                }
            }
        }

        private void textBoxSessionName_Validated(object sender, EventArgs e)
        {
            this.SetError(this.textBoxSessionName, String.Empty);
        }

        private void textBoxPort_Validating(object sender, CancelEventArgs e)
        {
            int val;
            ConnectionProtocol proto = this.protoTypesMap.ContainsKey(comboBoxProto.SelectedItem) ? (ConnectionProtocol)this.protoTypesMap[comboBoxProto.SelectedItem] : ConnectionProtocol.SSH;
            if (!Int32.TryParse(this.textBoxPort.Text, out val))
            {
                if (this.textBoxPort.Text == "")
                    if (proto == ConnectionProtocol.Mintty || proto == ConnectionProtocol.Cygterm || proto == ConnectionProtocol.RDP || proto == ConnectionProtocol.WINCMD || proto == ConnectionProtocol.PS || proto == ConnectionProtocol.WSL)
                        return;

                e.Cancel = true;
                this.SetError(this.textBoxPort, "Invalid Port");
            }
        }

        private void textBoxPort_Validated(object sender, EventArgs e)
        {
            this.SetError(this.textBoxPort, String.Empty);
        }

        private void textBoxHostname_Validating(object sender, CancelEventArgs e)
        {
            if (string.IsNullOrEmpty((string)this.comboBoxPuttyProfile.SelectedItem) &&
                string.IsNullOrEmpty(this.textBoxHostname.Text.Trim()))
            {
                if (sender == this.textBoxHostname)
                {
                    this.SetError(this.textBoxHostname, "A host name must be specified if a Putty Session Profile is not selected");
                }
                else if (sender == this.comboBoxPuttyProfile)
                {
                    this.SetError(this.comboBoxPuttyProfile, "A Putty Session Profile must be selected if a Host Name is not provided");
                }
            }
            else
            {
                this.SetError(this.textBoxHostname, String.Empty);
                this.SetError(this.comboBoxPuttyProfile, String.Empty);
            }
        }

        private void comboBoxPuttyProfile_Validating(object sender, CancelEventArgs e)
        {
            this.textBoxHostname_Validating(sender, e);
        }

        private void comboBoxPuttyProfile_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.ValidateChildren(ValidationConstraints.ImmediateChildren);
            this.ShowProfileMissing();
        }

        /// <summary>
        /// Draw one profile in the list, or the chosen one in the closed field: in red if it isn't saved in PuTTY or
        /// KiTTY (see <see cref="ShowProfileMissing"/>), otherwise in the normal colours
        /// </summary>
        /// <param name="sender">The profile list</param>
        /// <param name="e">The item and where to draw it</param>
        private void comboBoxPuttyProfile_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
                return;
            string profile = (string)this.comboBoxPuttyProfile.Items[e.Index];
            bool inField = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool highlighted = (e.State & DrawItemState.Selected) != 0 && !inField;
            // red on the blue highlight is hard to read, so a highlighted list item keeps the highlight text colour
            Color color = !IsProfileSaved(profile) && !highlighted ? placeholderMissingColor : e.ForeColor;
            TextRenderer.DrawText(e.Graphics, profile, e.Font, e.Bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>
        /// Under the profile list, say when the chosen profile isn't saved in PuTTY or KiTTY, e.g. a shared
        /// Sessions.xml naming a profile this user hasn't created yet. It still opens, with the defaults, so this is a
        /// note, not an error that blocks Save.
        /// </summary>
        private void ShowProfileMissing()
        {
            string profile = this.comboBoxPuttyProfile.SelectedItem as string;
            bool missing = !IsProfileSaved(profile);
            this.labelProfileMissing.Visible = missing;
            if (missing)
            {
                // set each time: DarkModeCS recolours labels during Load
                this.labelProfileMissing.ForeColor = placeholderMissingColor;
                string app = SuperPuTTY.PuTTYAppName;
                this.labelProfileMissing.Text = "Not saved in " + app + " yet";
                this.toolTip.SetToolTip(this.labelProfileMissing, string.Format(
                    "There's no {0} session named \"{1}\" (looked in {2}).\nUntil you save one with this name in {0}, the session opens with {0}'s Default Settings.",
                    app, profile, PuttyDataHelper.SessionStoreDescription));
            }
        }

        void SetError(Control control, string error)
        {
            this.errorProvider.SetError(control, error);
            this.EnableDisableSaveButton();
        }

        void EnableDisableSaveButton()
        {
            // a new session needs a name, and a host or profile, before the boxes have been left and checked
            bool filledIn = this.textBoxSessionName.Text.Trim() != String.Empty &&
                (this.textBoxHostname.Text.Trim() != String.Empty || !string.IsNullOrEmpty(this.comboBoxPuttyProfile.SelectedItem as string));
            this.buttonSave.Enabled = filledIn &&
                                      this.errorProvider.GetError(this.textBoxSessionName) == String.Empty &&
                                      this.errorProvider.GetError(this.textBoxHostname) == String.Empty &&
                                      this.errorProvider.GetError(this.textBoxPort) == String.Empty &&
                                      this.errorProvider.GetError(this.comboBoxPuttyProfile) == String.Empty &&
                                      this.errorProvider.GetError(this.comboBoxProto) == String.Empty;
        }

        #endregion

        private void buttonBrowse_Click(object sender, EventArgs e)
        {

            DialogResult dlgResult = this.openFileDialog1.ShowDialog();
            if (dlgResult == DialogResult.OK)
            {
                textBoxSPSLScriptFile.Text = this.openFileDialog1.FileName;
            }
        }

        private void buttonClearSPSLFile_Click(object sender, EventArgs e)
        {
            Session.SPSLFileName = textBoxSPSLScriptFile.Text = String.Empty;
            
        }

        private void buttonBrowseLocalPath_Click(object sender, EventArgs e)
        {            
            if (Directory.Exists(textBoxLocalPathSesion.Text))
            {
                folderBrowserDialog1.SelectedPath = textBoxLocalPathSesion.Text;
            }
            if (folderBrowserDialog1.ShowDialog(this) == DialogResult.OK)
            {
                if (!String.IsNullOrEmpty(folderBrowserDialog1.SelectedPath))
                    textBoxLocalPathSesion.Text = folderBrowserDialog1.SelectedPath;
            }


        }

        private void textBoxExtraArgs_Validating(object sender, CancelEventArgs e)
        {
            if (!string.IsNullOrEmpty(CommandLineOptions.getcommand(textBoxExtraArgs.Text, "-pw")))
            {
                e.Cancel = true;
                this.SetError(this.textBoxExtraArgs,  "Password set in clear text");
            }
        }

        private void textBoxExtraArgs_Validated(object sender, EventArgs e)
        {
            this.SetError(this.textBoxExtraArgs, String.Empty);
        }
    }
}
