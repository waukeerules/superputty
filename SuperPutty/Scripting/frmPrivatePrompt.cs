using System;
using System.Drawing;
using System.Windows.Forms;
using DarkModeForms;

namespace SuperPutty
{
    public class frmPrivatePrompt : Form
    {
        private Label displayMessage;
        private TextBox textBox1;

        /// <summary>
        /// Build the prompt SPSL's PWDPROMPT (hidden typing) and PROMPT (typing shown) ask with, in the interface's
        /// theme like the other dialogs
        /// </summary>
        /// <param name="arg">The message shown above the box</param>
        /// <param name="hidden">Whether the typing is hidden, for a password</param>
        public frmPrivatePrompt(String arg, bool hidden = true)
        {
            InitializeComponent(arg);
            textBox1.UseSystemPasswordChar = hidden;

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        private void InitializeComponent(String arg)
        {
            textBox1 = new TextBox();
            displayMessage = new Label();
            SuspendLayout();
            
            ClientSize = new Size(590, 185);

            displayMessage.Width = 310;
            displayMessage.Location = new Point(30,30);
            displayMessage.Text = arg;
            
            textBox1.UseSystemPasswordChar = true;
            textBox1.Width = ClientSize.Width - 50;
            textBox1.Location = new Point(20,ClientSize.Height - 55);

            Button okButton = new Button();
            okButton.DialogResult = DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new Size(90, 30);
            okButton.Text = "&OK";
            okButton.Anchor = AnchorStyles.Right;
            okButton.Location = new Point(ClientSize.Width - 95, 20);

            Button cancelButton = new Button();
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(90, 30);
            cancelButton.Text = "&Cancel";
            cancelButton.Anchor = AnchorStyles.Right;
            cancelButton.Location = new Point(ClientSize.Width - 95, 60);

            Controls.Add(displayMessage);
            Controls.Add(textBox1);
            Controls.Add(cancelButton);
            Controls.Add(okButton);

            FormBorderStyle = FormBorderStyle.FixedSingle;
            AcceptButton = okButton;
            CancelButton = cancelButton;
            Text = "SuperPutty";
            ResumeLayout(false);
            PerformLayout();
        }

        public String GetResult()
        {
            return textBox1.Text;
        }

        /// <summary>Fill the box in before it's shown, all selected so typing replaces it (Send Key > Other Key...)</summary>
        /// <param name="value">The text</param>
        public void SetValue(string value)
        {
            textBox1.Text = value ?? string.Empty;
            textBox1.SelectAll();
        }
    }
}