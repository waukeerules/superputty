using System;
using System.Drawing;
using System.Windows.Forms;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// The quick connect bar's list of hosts, as SuperXTerminal's HostList.svelte (its 43e4ba1): a box of several lines
    /// under the Host box, one host on each, for typing them or pasting rows copied from a spreadsheet. The Host box
    /// follows it as the list with commas between (Utils.QuickHosts), so closing it (Escape, the list button, a click
    /// elsewhere in the window) loses nothing; going to another program to copy doesn't close it. Ctrl+Enter connects,
    /// a tab for each host. Opened by the list button beside Host (frmSuperPutty).
    /// </summary>
    public class HostListPopup : ToolStripDropDown
    {
        /// <summary>The box of hosts, one on each line</summary>
        private readonly TextBox box;

        /// <summary>"3 hosts · Ctrl+Enter connects"</summary>
        private readonly Label count;

        /// <summary>Gives the Host box the hosts, with commas between, as they're typed</summary>
        private readonly Action<string> onChange;

        /// <summary>Connects, a tab for each host</summary>
        private readonly Action onConnect;

        /// <summary>Make the list with the hosts in the Host box, one on each line</summary>
        /// <param name="hosts">The Host box's text</param>
        /// <param name="onChange">Gives the Host box the hosts with commas between, as they change</param>
        /// <param name="onConnect">Connects (Ctrl+Enter); called after the list has closed</param>
        public HostListPopup(string hosts, Action<string> onChange, Action onConnect)
        {
            this.onChange = onChange;
            this.onConnect = onConnect;
            bool dark = SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;
            Color back = dark ? Color.FromArgb(37, 37, 38) : SystemColors.Control;
            Color fore = dark ? Color.FromArgb(230, 230, 230) : SystemColors.ControlText;
            Color muted = dark ? Color.FromArgb(150, 150, 150) : SystemColors.GrayText;

            this.box = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Dock = DockStyle.Top,
                Height = DpiUtils.ScaleWidth(150),
                Font = new Font("Lucida Console", 10f),
                Text = QuickHosts.AsLines(hosts),
                BackColor = dark ? Color.FromArgb(30, 30, 30) : SystemColors.Window,
                ForeColor = fore,
                BorderStyle = BorderStyle.FixedSingle
            };
            if (dark)
            {
                // the dark theme's scroll bar, as DarkModeCS gives the app's own boxes
                this.box.HandleCreated += (s, e) => NativeMethods.SetWindowTheme(this.box.Handle, "DarkMode_Explorer", null);
            }
            this.box.TextChanged += (s, e) => this.Changed();
            this.box.KeyDown += this.OnBoxKeyDown;
            Label note = new Label
            {
                Text = "One host on each line: each opens in a tab of its own, with the Login, Password and Profile beside.",
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(DpiUtils.ScaleWidth(320), 0),
                ForeColor = muted,
                Padding = new Padding(0, 4, 0, 0)
            };
            this.count = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = muted, Padding = new Padding(0, 2, 0, 0) };
            // a set size, worked out from its parts: a panel that sizes itself to controls docked in it shrinks to nothing
            int width = DpiUtils.ScaleWidth(340);
            Padding padding = new Padding(DpiUtils.ScaleWidth(6));
            int inner = width - padding.Horizontal;
            note.MaximumSize = new Size(inner, 0);
            int height = padding.Vertical + this.box.Height + note.GetPreferredSize(new Size(inner, 0)).Height
                + TextRenderer.MeasureText("0", this.count.Font).Height + this.count.Padding.Vertical + DpiUtils.ScaleWidth(4);
            Panel panel = new Panel { BackColor = back, Padding = padding, Size = new Size(width, height) };
            // docked to the top, the last added goes on top
            panel.Controls.Add(this.count);
            panel.Controls.Add(note);
            panel.Controls.Add(this.box);
            this.Items.Add(new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size });
            this.Padding = Padding.Empty;
            this.BackColor = back;
            this.ShowCount();
        }

        /// <summary>The hosts changed: the Host box follows, and the count</summary>
        private void Changed()
        {
            this.onChange(string.Join(", ", QuickHosts.Split(this.box.Text)));
            this.ShowCount();
        }

        /// <summary>Say how many hosts there are, and that Ctrl+Enter connects</summary>
        private void ShowCount()
        {
            int hosts = QuickHosts.Split(this.box.Text).Count;
            this.count.Text = hosts + (hosts == 1 ? " host" : " hosts") + " · Ctrl+Enter connects";
        }

        /// <summary>Ctrl+Enter connects; Escape closes the list</summary>
        /// <param name="sender">The box</param>
        /// <param name="e">The key</param>
        private void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                this.Close(ToolStripDropDownCloseReason.Keyboard);
                this.onConnect();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                this.Close(ToolStripDropDownCloseReason.Keyboard);
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                // a multi-line box doesn't select all on Ctrl+A by itself
                e.SuppressKeyPress = true;
                this.box.SelectAll();
            }
        }

        /// <summary>Put the keyboard in the box, at the end, once the list shows</summary>
        /// <param name="e">Not used</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.box.Focus();
            this.box.SelectionStart = this.box.TextLength;
        }

        /// <summary>Stay open while the user is in another program, copying hosts to paste here</summary>
        /// <param name="e">Why it's closing</param>
        protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange)
            {
                e.Cancel = true;
            }
            base.OnClosing(e);
        }
    }
}
