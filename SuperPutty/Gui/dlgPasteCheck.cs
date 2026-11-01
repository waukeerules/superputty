using System;
using System.Drawing;
using System.Windows.Forms;
using DarkModeForms;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Shows a paste of several lines before it's typed into a built-in terminal tab, so it can be checked and changed
    /// first: each line runs as it arrives, so 40 lines of config pasted by mistake into a switch would all be applied.
    /// TerminalControl.Paste opens it when the paste has at least Options -> Terminal's number of lines
    /// (TerminalPreferences "pasteLines"); Paste types the text as it is in the box. Built in code.
    /// </summary>
    public class dlgPasteCheck : Form
    {
        /// <summary>What's about to be pasted, and how many lines that is now</summary>
        private readonly Label heading = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };

        /// <summary>The text to paste, which can be changed</summary>
        private readonly TextBox text = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 10f)
        };

        /// <summary>Types the text into the session</summary>
        private readonly Button buttonPaste = new Button { Text = "&Paste", AutoSize = true, DialogResult = DialogResult.OK };

        /// <summary>Pastes nothing</summary>
        private readonly Button buttonCancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        /// <summary>The session's name, for the heading</summary>
        private readonly string sessionName;

        /// <summary>Build the dialog around the text</summary>
        /// <param name="pasteText">The clipboard text (or the menu's Copy and Paste selection)</param>
        /// <param name="sessionName">The session it's going into</param>
        public dlgPasteCheck(string pasteText, string sessionName)
        {
            this.sessionName = sessionName;
            this.Text = "Paste";
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Padding = new Padding(12);
            this.CancelButton = this.buttonCancel;
            this.KeyPreview = true;

            // the box shows Windows line breaks; a lone CR or LF would show as nothing
            this.text.Text = (pasteText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            buttons.Controls.AddRange(new Control[] { this.buttonCancel, this.buttonPaste });
            this.Controls.Add(this.text);
            this.Controls.Add(buttons);
            this.Controls.Add(this.heading);
            // sizes for 100%, converted for the monitor (a form built in code isn't scaled by WinForms)
            this.ClientSize = new Size(this.LogicalToDeviceUnits(680), this.LogicalToDeviceUnits(360));
            this.MinimumSize = new Size(this.LogicalToDeviceUnits(400), this.LogicalToDeviceUnits(220));

            ToolTip tips = new ToolTip();
            tips.SetToolTip(this.buttonPaste, "Type the text as it is in the box (Ctrl+Enter)");
            tips.SetToolTip(this.text, "Change the text here before pasting it; Enter adds a line, Ctrl+Enter pastes");
            this.text.TextChanged += (s, e) => this.UpdateHeading();
            this.UpdateHeading();

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>The text as it is in the box now, with Windows line breaks</summary>
        public string PasteText
        {
            get { return this.text.Text; }
        }

        /// <summary>
        /// Start in the box with the caret at the start and nothing selected, so the text can be read and changed at
        /// once, and a key pressed by mistake doesn't replace it all or paste it
        /// </summary>
        /// <param name="e">Not used</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.text.Focus();
            this.text.Select(0, 0);
            this.text.ScrollToCaret();
        }

        /// <summary>Ctrl+Enter pastes: Enter itself adds a line in the box</summary>
        /// <param name="e">The key</param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                this.DialogResult = DialogResult.OK;
                return;
            }
            base.OnKeyDown(e);
        }

        /// <summary>Show how many lines the text is now, and disable Paste when there's nothing to paste</summary>
        private void UpdateHeading()
        {
            int lines = TerminalControl.PasteLineCount(this.text.Text);
            this.heading.Text = string.Format("Paste {0} line{1} into {2}? Check the text, and change it here if needed.",
                lines, lines == 1 ? "" : "s", this.sessionName);
            this.buttonPaste.Enabled = this.text.TextLength > 0;
        }
    }
}
